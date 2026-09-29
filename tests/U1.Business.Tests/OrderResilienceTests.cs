using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class OrderResilienceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("checkout", "CHECKOUT_RETRY")]
    [InlineData("status", "ORDER_STATUS_RETRY")]
    public async Task Three_sql_timeouts_return_503_and_rollback_the_whole_attempt(
        string operation, string expectedCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetCheckoutState();
        var product = await CreateProduct(5);
        var dealer = await CreateDealer(product.Id, 2);
        var requestId = Guid.NewGuid();
        var input = new CheckoutInput(requestId, "CI timeout", [new(product.Id, 2, product.Price)]);
        var interceptor = new SqlTimeoutAtSaveInterceptor(
            operation == "checkout" ? "[OrderItems]" : "UPDATE [Orders]");
        var service = CreateService(interceptor);

        int? orderId = null;
        if (operation == "status")
        {
            var created = await CreateService().Checkout(dealer.UserId, input);
            orderId = ApiTest.Property(JsonSerializer.SerializeToElement(created), "id").GetInt32();
        }

        var error = await Assert.ThrowsAsync<BusinessException>(() => operation == "checkout"
            ? service.Checkout(dealer.UserId, input)
            : service.ChangeStatus(orderId!.Value, "Reddedildi", 1));

        Assert.Equal(503, error.Status);
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(3, interceptor.FaultCount);

        await using var db = await factory.Services
            .GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(cancellationToken);
        Assert.Equal(operation == "checkout" ? 0 : 1,
            await db.Orders.CountAsync(x => x.RequestId == requestId, cancellationToken));
        Assert.Equal(operation == "checkout" ? 5 : 3,
            await db.Products.Where(x => x.Id == product.Id)
                .Select(x => x.Stock).SingleAsync(cancellationToken));
        Assert.Equal(operation == "checkout" ? 2 : 0,
            await db.CartItems.Where(x => x.CartId == dealer.CartId)
                .Select(x => x.Quantity).SingleOrDefaultAsync(cancellationToken));
        if (orderId is not null)
            Assert.Equal("Bekliyor", await db.Orders.Where(x => x.Id == orderId)
                .Select(x => x.Status).SingleAsync(cancellationToken));
        Assert.Equal(operation == "checkout" ? 0 : 1,
            await db.OrderItems.CountAsync(x => x.ProductId == product.Id, cancellationToken));
        Assert.Equal(operation == "checkout" ? 0 : 1,
            await db.StockMovements.CountAsync(
                x => x.ProductId == product.Id && x.MovementType == "OrderPlaced",
                cancellationToken));
        Assert.Equal(0, await db.StockMovements.CountAsync(
            x => x.ProductId == product.Id && x.MovementType == "OrderRejected",
            cancellationToken));
        Assert.Equal(0, await db.AdminEvents.CountAsync(
            x => x.EntityType == "Order" && x.EntityId == orderId,
            cancellationToken));
    }

    [Fact]
    public async Task A_lost_success_response_retried_with_the_same_request_id_returns_the_committed_order()
    {
        await factory.ResetCheckoutState();
        var product = await CreateProduct(3);
        var dealer = await CreateDealer(product.Id, 2);
        var requestId = Guid.NewGuid();
        var input = new CheckoutInput(requestId, "CI lost response", [new(product.Id, 2, product.Price)]);
        var service = CreateService();

        // The first commit succeeds, but its response is deliberately discarded by the caller.
        await service.Checkout(dealer.UserId, input);
        var recovered = await service.Checkout(dealer.UserId, input);
        var recoveredId = ApiTest.Property(JsonSerializer.SerializeToElement(recovered), "id").GetInt32();

        await using var db = await factory.Services
            .GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(recoveredId, await db.Orders.Where(x => x.RequestId == requestId)
            .Select(x => x.Id).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.OrderItems.CountAsync(x => x.OrderId == recoveredId, TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.StockMovements.CountAsync(
            x => x.OrderId == recoveredId && x.MovementType == "OrderPlaced",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_same_request_creates_one_order_and_deducts_stock_once()
    {
        await factory.ResetCheckoutState();
        var product = await CreateProduct(2);
        var dealer = await CreateDealer(product.Id, 2);
        var requestId = Guid.NewGuid();
        var input = new CheckoutInput(requestId, "CI concurrent same request", [new(product.Id, 2, product.Price)]);

        var results = await RunTogether(
            () => CreateService().Checkout(dealer.UserId, input),
            () => CreateService().Checkout(dealer.UserId, input));
        var ids = results.Select(x => ApiTest.Property(JsonSerializer.SerializeToElement(x), "id").GetInt32());
        Assert.Single(ids.Distinct());

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Orders.CountAsync(x => x.RequestId == requestId, TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_dealers_cannot_share_a_request_id_or_oversell_the_last_unit()
    {
        await factory.ResetCheckoutState();
        var product = await CreateProduct(1);
        var first = await CreateDealer(product.Id, 1);
        var second = await CreateDealer(product.Id, 1);
        var requestId = Guid.NewGuid();
        var firstInput = new CheckoutInput(requestId, "CI first", [new(product.Id, 1, product.Price)]);
        var secondInput = new CheckoutInput(requestId, "CI second", [new(product.Id, 1, product.Price)]);

        var firstOrder = await CreateService().Checkout(first.UserId, firstInput);
        var conflict = await Assert.ThrowsAsync<BusinessException>(() =>
            CreateService().Checkout(second.UserId, secondInput));
        Assert.Equal(409, conflict.Status);
        Assert.Equal("REQUEST_ID_CONFLICT", conflict.Code);

        var stockConflict = await Assert.ThrowsAsync<BusinessException>(() =>
            CreateService().Checkout(second.UserId, secondInput with { RequestId = Guid.NewGuid() }));
        Assert.Equal("INSUFFICIENT_STOCK", stockConflict.Code);

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Orders.CountAsync(x => x.RequestId == requestId, TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.CartItems.Where(x => x.CartId == second.CartId)
            .Select(x => x.Quantity).SingleAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(firstOrder);
    }

    [Fact]
    public async Task Concurrent_dealers_competing_for_the_last_unit_cannot_oversell()
    {
        await factory.ResetCheckoutState();
        var product = await CreateProduct(1);
        var first = await CreateDealer(product.Id, 1);
        var second = await CreateDealer(product.Id, 1);
        var firstRequest = Guid.NewGuid();
        var secondRequest = Guid.NewGuid();
        async Task<BusinessException?> TryCheckout(int userId, Guid requestId)
        {
            try
            {
                await CreateService().Checkout(userId,
                    new CheckoutInput(requestId, "CI last stock", [new(product.Id, 1, product.Price)]));
                return null;
            }
            catch (BusinessException error)
            {
                return error;
            }
        }

        var results = await RunTogether(
            () => TryCheckout(first.UserId, firstRequest),
            () => TryCheckout(second.UserId, secondRequest));
        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x?.Code == "INSUFFICIENT_STOCK");

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Orders.CountAsync(x => x.RequestId == firstRequest || x.RequestId == secondRequest, TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejection_racing_with_checkout_returns_stock_once()
    {
        await factory.ResetCheckoutState();
        var product = await CreateProduct(1);
        var first = await CreateDealer(product.Id, 1);
        var second = await CreateDealer(product.Id, 1);
        var firstOrder = await CreateService().Checkout(first.UserId,
            new CheckoutInput(Guid.NewGuid(), "CI order to reject", [new(product.Id, 1, product.Price)]));
        var firstId = ApiTest.Property(JsonSerializer.SerializeToElement(firstOrder), "id").GetInt32();
        var secondRequest = Guid.NewGuid();
        async Task<bool> Reject()
        {
            await CreateService().ChangeStatus(firstId, "Reddedildi", 1);
            return true;
        }
        async Task<bool> TryCheckout()
        {
            try
            {
                await CreateService().Checkout(second.UserId,
                    new CheckoutInput(secondRequest, "CI racing checkout", [new(product.Id, 1, product.Price)]));
                return true;
            }
            catch (BusinessException error) when (error.Code == "INSUFFICIENT_STOCK")
            {
                return false;
            }
        }
        var outcomes = await RunTogether(Reject, TryCheckout);
        var checkoutSucceeded = outcomes[1];

        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Reddedildi", await db.Orders.Where(x => x.Id == firstId)
            .Select(x => x.Status).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(checkoutSucceeded ? 0 : 1,
            await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(checkoutSucceeded ? 1 : 0,
            await db.Orders.CountAsync(x => x.RequestId == secondRequest, TestContext.Current.CancellationToken));
        await CreateService().ChangeStatus(firstId, "Reddedildi", 1);
        Assert.Equal(checkoutSucceeded ? 0 : 1,
            await db.Products.Where(x => x.Id == product.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Multi_product_checkout_and_rejection_change_each_stock_once()
    {
        await factory.ResetCheckoutState();
        var first = await CreateProduct(2);
        var second = await CreateProduct(3);
        var dealer = await CreateDealer(first.Id, 2);
        // Set the second line directly in this isolated test database.
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.CartItems.Add(new CartItem { CartId = dealer.CartId, ProductId = second.Id, Quantity = 3 });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var input = new CheckoutInput(Guid.NewGuid(), "CI multiple products",
            [new(first.Id, 2, first.Price), new(second.Id, 3, second.Price)]);
        var result = await CreateService().Checkout(dealer.UserId, input);
        var id = ApiTest.Property(JsonSerializer.SerializeToElement(result), "id").GetInt32();
        await CreateService().ChangeStatus(id, "Reddedildi", 1);
        await CreateService().ChangeStatus(id, "Reddedildi", 1);

        await using var check = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, await check.OrderItems.CountAsync(x => x.OrderId == id, TestContext.Current.CancellationToken));
        Assert.Equal(2, await check.Products.Where(x => x.Id == first.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, await check.Products.Where(x => x.Id == second.Id).Select(x => x.Stock).SingleAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, await check.StockMovements.CountAsync(
            x => x.OrderId == id && x.MovementType == "OrderPlaced",
            TestContext.Current.CancellationToken));
        Assert.Equal(2, await check.StockMovements.CountAsync(
            x => x.OrderId == id && x.MovementType == "OrderRejected",
            TestContext.Current.CancellationToken));
    }

    private OrderService CreateService(DbCommandInterceptor? interceptor = null)
    {
        IDbContextFactory<BusinessDbContext> dbFactory = interceptor is null
            ? factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            : new InterceptedDbFactory(ApiFactory.ConnectionString, interceptor);
        return new OrderService(dbFactory, new ConfigurationBuilder().Build());
    }

    private static async Task<T[]> RunTogether<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        Task<T> Schedule(Func<Task<T>> action) => Task.Run(async () =>
        {
            if (Interlocked.Increment(ref readyCount) == 2)
                ready.SetResult();
            await start.Task;
            return await action();
        }, TestContext.Current.CancellationToken);

        var tasks = new[] { Schedule(first), Schedule(second) };
        await ready.Task;
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private async Task<(int Id, decimal Price)> CreateProduct(int stock)
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var product = new Product
        {
            Code = "CI-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Name = "CI resilience product",
            Description = "CI",
            Brand = "CI",
            ManufacturerCode = "CI",
            ImageUrl = "/images/product.svg",
            Stock = stock,
            CriticalStock = 1,
            Price = 100m,
            CategoryId = 1
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (product.Id, product.Price);
    }

    private async Task<(int UserId, int CartId)> CreateDealer(int productId, int quantity)
    {
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var user = new User
        {
            FirstName = "CI", LastName = "Dealer",
            Email = $"ci-{Guid.NewGuid():N}@example.test",
            Phone = "05321234567", Company = "CI", PasswordHash = "test-only",
            Role = "Dealer", IsActive = true, AuthVersion = 1
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var cart = new Cart { UserId = user.Id };
        db.Carts.Add(cart);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.CartItems.Add(new CartItem { CartId = cart.Id, ProductId = productId, Quantity = quantity });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (user.Id, cart.Id);
    }

    private sealed class InterceptedDbFactory(string connectionString, DbCommandInterceptor interceptor)
        : IDbContextFactory<BusinessDbContext>
    {
        private readonly DbContextOptions<BusinessDbContext> options =
            new DbContextOptionsBuilder<BusinessDbContext>()
                .UseSqlServer(connectionString)
                .AddInterceptors(interceptor)
                .Options;

        public BusinessDbContext CreateDbContext() => new(options);
    }

    private sealed class SqlTimeoutAtSaveInterceptor(string commandFragment) : DbCommandInterceptor
    {
        public int FaultCount { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await FailMatchingCommand(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await FailMatchingCommand(command, cancellationToken);
            return result;
        }

        private async Task FailMatchingCommand(DbCommand command, CancellationToken cancellationToken)
        {
            if (!command.CommandText.Contains(commandFragment, StringComparison.OrdinalIgnoreCase))
                return;

            FaultCount++;
            using var timeout = ((SqlConnection)command.Connection!).CreateCommand();
            timeout.Transaction = (SqlTransaction)command.Transaction!;
            timeout.CommandText = "WAITFOR DELAY '00:00:02'";
            timeout.CommandTimeout = 1;
            await timeout.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
