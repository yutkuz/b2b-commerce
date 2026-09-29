using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Testing;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace U1.Business.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..12];

    public static string DatabaseName { get; } = $"U1Business_CI_{RunId}";

    public static string ConnectionString { get; } =
        $@"Server=(localdb)\MSSQLLocalDB;Database={DatabaseName};Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("U1_TEST_DATABASE", DatabaseName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = ConnectionString,
                ["DatabaseLocks:OrderTimeoutMilliseconds"] = "250"
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await TestDatabaseLifecycle.DropUncancellableAsync(DatabaseName, "U1Business_CI_");
            if (string.Equals(
                    Environment.GetEnvironmentVariable("U1_TEST_DATABASE"),
                    DatabaseName,
                    StringComparison.Ordinal))
            {
                Environment.SetEnvironmentVariable("U1_TEST_DATABASE", null);
            }
        }
    }

    public async Task ResetCheckoutState()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();

        await db.OrderItems.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.Orders.ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        var testUsers = db.Users.Where(u => EF.Functions.Like(u.Email, "ci-%@example.test"));
        var testUserIds = testUsers.Select(u => u.Id);
        var testCarts = db.Carts.Where(c => testUserIds.Contains(c.UserId));
        var testCartIds = testCarts.Select(c => c.Id);

        await db.CartItems.Where(ci => testCartIds.Contains(ci.CartId)).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await testCarts.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await testUsers.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        await db.Products
            .Where(p => p.Code == "DG-001")
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Stock, 24), TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiTestCollection
{
    public const string Name = "API integration tests";
}

[Collection(ApiTestCollection.Name)]
public sealed class ApiSecurityAndBusinessRulesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Test_database_is_unique_and_isolated_from_the_default_database()
    {
        using var client = NewClient();

        var environment = await client.GetFromJsonAsync<JsonElement>("/api/test-environment", TestContext.Current.CancellationToken);

        Assert.StartsWith("U1Business_CI_", ApiFactory.DatabaseName);
        Assert.NotEqual("U1Business", ApiFactory.DatabaseName);
        Assert.Equal(ApiFactory.DatabaseName, ApiTest.Property(environment, "database").GetString());
    }

    [Fact]
    public async Task Authentication_csrf_and_admin_authorization_are_enforced()
    {
        using var guest = NewClient();
        var anonymousCatalog = await guest.GetAsync("/api/products", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousCatalog.StatusCode);

        var anonymousToken = await ApiTest.GetCsrf(guest);
        var invalidPhone = await ApiTest.SendJson(guest, HttpMethod.Post, "/api/auth/register", new
        {
            firstName = "CI",
            lastName = "Invalid",
            email = $"ci-{Guid.NewGuid():N}@example.test",
            phone = "----------",
            company = "CI",
            password = "CITest!2026"
        }, anonymousToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPhone.StatusCode);

        var email = $"ci-{Guid.NewGuid():N}@example.test";
        var register = await ApiTest.SendJson(guest, HttpMethod.Post, "/api/auth/register", new
        {
            firstName = "CI",
            lastName = "Dealer",
            email,
            phone = "05321234567",
            company = "CI",
            password = "CITest!2026"
        }, anonymousToken);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var products = await guest.GetFromJsonAsync<JsonElement>("/api/products?q=DG-001", TestContext.Current.CancellationToken);
        var productId = ApiTest.Property(ApiTest.Property(products, "items")[0], "id").GetInt32();

        var withoutCsrf = await ApiTest.SendJson(
            guest,
            HttpMethod.Post,
            "/api/cart",
            new { productId, quantity = 1 },
            csrf: null);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        var dealerToken = await ApiTest.GetCsrf(guest);
        var forbidden = await ApiTest.SendJson(guest, HttpMethod.Post, "/api/admin/products", ApiTest.ProductPayload(), dealerToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var nullCart = await ApiTest.SendJson(guest, HttpMethod.Post, "/api/cart", body: null, dealerToken);
        Assert.Equal(HttpStatusCode.BadRequest, nullCart.StatusCode);

        var missingProduct = await ApiTest.SendJson(guest, HttpMethod.Post, "/api/cart",
            new { productId = int.MaxValue, quantity = 1 }, dealerToken);
        Assert.Equal(HttpStatusCode.NotFound, missingProduct.StatusCode);
    }

    [Fact]
    public async Task Product_validation_search_and_rowversion_concurrency_are_preserved()
    {
        using var admin = NewClient();
        var adminToken = await ApiTest.LoginAdmin(admin);
        var code = "CI-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var invalidPrecision = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
            ApiTest.ProductPayload(code, price: 100.123m), adminToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPrecision.StatusCode);

        var negativeStock = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
            ApiTest.ProductPayload(code, stock: -1), adminToken);
        Assert.Equal(HttpStatusCode.BadRequest, negativeStock.StatusCode);

        var created = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
            ApiTest.ProductPayload(code), adminToken);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var productId = ApiTest.Property(createdBody, "id").GetInt32();

        var search = await admin.GetFromJsonAsync<JsonElement>("/api/products?q=CI-SPECIAL", TestContext.Current.CancellationToken);
        Assert.Contains(ApiTest.Property(search, "items").EnumerateArray(),
            item => ApiTest.Property(item, "id").GetInt32() == productId);

        var initial = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        var version = ApiTest.Property(initial, "rowVersion").GetString();

        var update = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/products/{productId}",
            ApiTest.ProductPayload(code, stock: 4, version: version), adminToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var stale = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/products/{productId}",
            ApiTest.ProductPayload(code, stock: 3, version: version), adminToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("PRODUCT_CHANGED", ApiTest.Property(staleBody, "code").GetString());
    }

    [Fact]
    public async Task Checkout_idempotency_price_snapshot_authorization_and_stock_restore_are_preserved()
    {
        await factory.ResetCheckoutState();

        using var admin = NewClient();
        var adminToken = await ApiTest.LoginAdmin(admin);
        var code = "CI-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        var created = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
            ApiTest.ProductPayload(code, stock: 5, price: 100m), adminToken);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var productId = ApiTest.Property(createdBody, "id").GetInt32();

        using var buyer = NewClient();
        var buyerToken = await ApiTest.RegisterDealer(buyer);

        var add = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/cart",
            new { productId, quantity = 2 }, buyerToken);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var cart = await buyer.GetFromJsonAsync<JsonElement>("/api/cart", TestContext.Current.CancellationToken);
        Assert.Equal(2, ApiTest.Property(cart, "count").GetInt32());
        Assert.Equal(200m, ApiTest.Property(cart, "total").GetDecimal());

        var initial = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        var reduceStock = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/products/{productId}",
            ApiTest.ProductPayload(code, stock: 1, price: 100m,
                version: ApiTest.Property(initial, "rowVersion").GetString()), adminToken);
        Assert.Equal(HttpStatusCode.OK, reduceStock.StatusCode);

        var requestId = Guid.NewGuid();
        var checkoutBody = new
        {
            requestId,
            note = "CI order",
            lines = new[] { new { productId, quantity = 2, unitPrice = 100m } }
        };

        var insufficient = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/orders", checkoutBody, buyerToken);
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);
        var preservedCart = await buyer.GetFromJsonAsync<JsonElement>("/api/cart", TestContext.Current.CancellationToken);
        Assert.Equal(2, ApiTest.Property(preservedCart, "count").GetInt32());

        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        var restoreStock = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/products/{productId}",
            ApiTest.ProductPayload(code, stock: 5, price: 100m,
                version: ApiTest.Property(current, "rowVersion").GetString()), adminToken);
        Assert.Equal(HttpStatusCode.OK, restoreStock.StatusCode);

        var orderResponse = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/orders", checkoutBody, buyerToken);
        Assert.Equal(HttpStatusCode.OK, orderResponse.StatusCode);
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var orderId = ApiTest.Property(order, "id").GetInt32();

        var repeated = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/orders", checkoutBody, buyerToken);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var repeatedOrder = await repeated.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(orderId, ApiTest.Property(repeatedOrder, "id").GetInt32());

        var afterCheckout = await buyer.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        Assert.Equal(3, ApiTest.Property(afterCheckout, "stock").GetInt32());
        var emptyCart = await buyer.GetFromJsonAsync<JsonElement>("/api/cart", TestContext.Current.CancellationToken);
        Assert.Equal(0, ApiTest.Property(emptyCart, "count").GetInt32());

        var beforePriceChange = await admin.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        var priceChange = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/products/{productId}",
            ApiTest.ProductPayload(code, stock: 3, price: 120m,
                version: ApiTest.Property(beforePriceChange, "rowVersion").GetString()), adminToken);
        Assert.Equal(HttpStatusCode.OK, priceChange.StatusCode);

        var orderDetail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", TestContext.Current.CancellationToken);
        Assert.Equal(100m,
            ApiTest.Property(ApiTest.Property(orderDetail, "items")[0], "unitPrice").GetDecimal());

        var reject = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/orders/{orderId}/status",
            new { status = "Reddedildi" }, adminToken);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var rejectedOrder = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", TestContext.Current.CancellationToken);
        Assert.Equal("Reddedildi",
            ApiTest.Property(ApiTest.Property(rejectedOrder, "order"), "status").GetString());
        var afterReject = await buyer.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        Assert.Equal(5, ApiTest.Property(afterReject, "stock").GetInt32());

        var repeatReject = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/orders/{orderId}/status",
            new { status = "Reddedildi" }, adminToken);
        Assert.Equal(HttpStatusCode.OK, repeatReject.StatusCode);
        var afterRepeatReject = await buyer.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
        Assert.Equal(5, ApiTest.Property(afterRepeatReject, "stock").GetInt32());

        using var otherDealer = NewClient();
        await ApiTest.RegisterDealer(otherDealer);
        var foreignOrder = await otherDealer.GetAsync($"/api/orders/{orderId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, foreignOrder.StatusCode);
    }

    [Fact]
    public async Task Admin_grid_banner_and_user_session_invalidation_are_preserved()
    {
        using var admin = NewClient();
        var adminToken = await ApiTest.LoginAdmin(admin);

        var grid = await admin.GetFromJsonAsync<JsonElement>("/api/admin/grid", TestContext.Current.CancellationToken);
        var gridUpdate = await ApiTest.SendJson(admin, HttpMethod.Put, "/api/admin/grid", grid, adminToken);
        Assert.Equal(HttpStatusCode.OK, gridUpdate.StatusCode);

        var banner = new
        {
            title = "CI duyuru",
            subtitle = "İlk metin",
            buttonText = "Ürünleri aç",
            searchTerm = "CI",
            isActive = true,
            position = 3
        };
        var bannerCreate = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/banners", banner, adminToken);
        Assert.Equal(HttpStatusCode.OK, bannerCreate.StatusCode);
        var bannerCreated = await bannerCreate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var bannerId = ApiTest.Property(bannerCreated, "id").GetInt32();

        var bannerUpdate = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/banners/{bannerId}", new
        {
            banner.title,
            subtitle = "Güncel metin",
            banner.buttonText,
            banner.searchTerm,
            banner.isActive,
            banner.position
        }, adminToken);
        Assert.Equal(HttpStatusCode.OK, bannerUpdate.StatusCode);

        var banners = await admin.GetFromJsonAsync<JsonElement>("/api/admin/banners", TestContext.Current.CancellationToken);
        Assert.Contains(banners.EnumerateArray(), item =>
            ApiTest.Property(item, "id").GetInt32() == bannerId
            && ApiTest.Property(item, "subtitle").GetString() == "Güncel metin");

        using var dealer = NewClient();
        var dealerEmail = $"ci-{Guid.NewGuid():N}@example.test";
        await ApiTest.RegisterDealer(dealer, dealerEmail);

        var users = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/users?q=" + Uri.EscapeDataString(dealerEmail), TestContext.Current.CancellationToken);
        var managed = ApiTest.Property(users, "items").EnumerateArray()
            .Single(x => ApiTest.Property(x, "email").GetString() == dealerEmail);

        var userUpdate = await ApiTest.SendJson(admin, HttpMethod.Put,
            $"/api/admin/users/{ApiTest.Property(managed, "id").GetInt32()}", new
            {
                firstName = ApiTest.Property(managed, "firstName").GetString(),
                lastName = ApiTest.Property(managed, "lastName").GetString(),
                email = ApiTest.Property(managed, "email").GetString(),
                phone = ApiTest.Property(managed, "phone").GetString(),
                company = "Updated CI Dealer",
                isActive = true,
                newPassword = "",
                version = ApiTest.Property(managed, "version").GetInt32()
            }, adminToken);
        Assert.Equal(HttpStatusCode.OK, userUpdate.StatusCode);

        var oldSession = await dealer.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
    }

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
}

[Collection(ApiTestCollection.Name)]
public sealed class CheckoutConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Checkout_lock_timeout_returns_controlled_error_and_rolls_back_every_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await factory.ResetCheckoutState();

        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminToken = await ApiTest.LoginAdmin(admin);
        var code = "CI-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var created = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
            ApiTest.ProductPayload(code, stock: 5, price: 100m), adminToken);
        created.EnsureSuccessStatusCode();
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var productId = ApiTest.Property(createdBody, "id").GetInt32();

        using var buyer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var buyerEmail = $"ci-{Guid.NewGuid():N}@example.test";
        var buyerToken = await ApiTest.RegisterDealer(buyer, buyerEmail);
        var add = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/cart",
            new { productId, quantity = 2 }, buyerToken);
        add.EnsureSuccessStatusCode();

        var requestId = Guid.NewGuid();
        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(cancellationToken);
        var lockResource = "U1Business.Order." + requestId.ToString("N");
        using var acquire = blocker.CreateCommand();
        acquire.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @result;
            """;
        acquire.Parameters.AddWithValue("@resource", lockResource);
        Assert.True(Convert.ToInt32(await acquire.ExecuteScalarAsync(cancellationToken)) >= 0);

        try
        {
            var response = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/orders", new
            {
                requestId,
                note = "CI locked order",
                lines = new[] { new { productId, quantity = 2, unitPrice = 100m } }
            }, buyerToken);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            Assert.Equal("REQUEST_BUSY", ApiTest.Property(error, "code").GetString());

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.False(await db.Orders.AnyAsync(x => x.RequestId == requestId, cancellationToken));
            Assert.Equal(5, await db.Products
                .Where(x => x.Id == productId)
                .Select(x => x.Stock)
                .SingleAsync(cancellationToken));
            Assert.Equal(2, await (
                from item in db.CartItems
                join cart in db.Carts on item.CartId equals cart.Id
                join user in db.Users on cart.UserId equals user.Id
                where item.ProductId == productId && user.Email == buyerEmail
                select item.Quantity).SingleAsync(cancellationToken));
        }
        finally
        {
            await ApiTest.ReleaseSessionLock(blocker, lockResource, cancellationToken);
        }
    }

    [Fact]
    public async Task Eight_concurrent_dealers_can_checkout_without_server_errors()
    {
        await factory.ResetCheckoutState();

        var clients = Enumerable.Range(0, 8)
            .Select(_ => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }))
            .ToArray();

        try
        {
            var sessionTokens = new string[clients.Length];
            for (var i = 0; i < clients.Length; i++)
            {
                sessionTokens[i] = await ApiTest.RegisterDealer(clients[i]);
            }

            var products = await clients[0].GetFromJsonAsync<JsonElement>("/api/products?q=DG-001", TestContext.Current.CancellationToken);
            var product = ApiTest.Property(products, "items")[0];
            var productId = ApiTest.Property(product, "id").GetInt32();
            var price = ApiTest.Property(product, "price").GetDecimal();

            for (var i = 0; i < clients.Length; i++)
            {
                var cart = await ApiTest.SendJson(clients[i], HttpMethod.Post, "/api/cart",
                    new { productId, quantity = 1 }, sessionTokens[i]);
                Assert.Equal(HttpStatusCode.OK, cart.StatusCode);
            }

            var requests = clients.Select((client, i) =>
                ApiTest.SendJson(client, HttpMethod.Post, "/api/orders", new
                {
                    requestId = Guid.NewGuid(),
                    note = "CI concurrent checkout",
                    lines = new[] { new { productId, quantity = 1, unitPrice = price } }
                }, sessionTokens[i]));

            var responses = await Task.WhenAll(requests);

            var failures = new List<string>();
            foreach (var response in responses)
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    failures.Add($"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
                response.Dispose();
            }

            Assert.True(failures.Count == 0, "Concurrent checkout failures:\n" + string.Join("\n", failures));

            var refreshed = await clients[0].GetFromJsonAsync<JsonElement>($"/api/products/{productId}", TestContext.Current.CancellationToken);
            Assert.Equal(16, ApiTest.Property(refreshed, "stock").GetInt32());
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }
    }
}

[Collection(ApiTestCollection.Name)]
public sealed class DatabaseApplicationLockTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Database_creation_lock_timeout_stops_before_schema_or_data_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await ReadDatabaseState(cancellationToken);

        var builder = new SqlConnectionStringBuilder(ApiFactory.ConnectionString)
        {
            InitialCatalog = "master"
        };
        await using var blocker = new SqlConnection(builder.ConnectionString);
        await blocker.OpenAsync(cancellationToken);
        await AcquireSessionLock(
            blocker,
            "U1Business.Create." + ApiFactory.DatabaseName,
            cancellationToken);

        var lockResource = "U1Business.Create." + ApiFactory.DatabaseName;
        try
        {
            var database = CreateDatabaseWithShortLockTimeout();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Initialize(development: true));

            Assert.Equal("Veritabanı oluşturma kilidi alınamadı.", error.Message);
            Assert.Equal(before, await ReadDatabaseState(cancellationToken));
        }
        finally
        {
            await ApiTest.ReleaseSessionLock(blocker, lockResource, cancellationToken);
        }
    }

    [Fact]
    public async Task Schema_lock_timeout_rolls_back_without_schema_or_data_changes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await ReadDatabaseState(cancellationToken);

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(cancellationToken);
        await AcquireSessionLock(blocker, "U1Business.Schema", cancellationToken);

        const string lockResource = "U1Business.Schema";
        try
        {
            var database = CreateDatabaseWithShortLockTimeout();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => database.Initialize(development: true));

            Assert.Equal("Şema yükseltme kilidi alınamadı.", error.Message);
            Assert.Equal(before, await ReadDatabaseState(cancellationToken));
        }
        finally
        {
            await ApiTest.ReleaseSessionLock(blocker, lockResource, cancellationToken);
        }
    }

    private async Task<(int SchemaVersion, int Products, int Orders)> ReadDatabaseState(
        CancellationToken cancellationToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        return (
            await db.SchemaVersions.MaxAsync(x => x.Version, cancellationToken),
            await db.Products.CountAsync(cancellationToken),
            await db.Orders.CountAsync(cancellationToken));
    }

    private Database CreateDatabaseWithShortLockTimeout()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = ApiFactory.ConnectionString,
                ["DatabaseLocks:InitializationTimeoutMilliseconds"] = "250"
            })
            .Build();
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>();
        return new Database(config, dbFactory);
    }

    private static async Task AcquireSessionLock(
        SqlConnection connection,
        string resource,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 0;
            SELECT @result;
            """;
        command.Parameters.AddWithValue("@resource", resource);
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) >= 0);
    }

}

[Collection(ApiTestCollection.Name)]
public sealed class UserConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Stale_admin_profile_update_cannot_revert_a_new_password()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminToken = await ApiTest.LoginAdmin(admin);

        using var dealerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var dealerEmail = $"ci-{Guid.NewGuid():N}@example.test";
        await ApiTest.RegisterDealer(dealerClient, dealerEmail);

        var users = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/users?q=" + Uri.EscapeDataString(dealerEmail), TestContext.Current.CancellationToken);
        var dealer = ApiTest.Property(users, "items").EnumerateArray()
            .Single(x => ApiTest.Property(x, "email").GetString() == dealerEmail);

        var id = ApiTest.Property(dealer, "id").GetInt32();
        var version = ApiTest.Property(dealer, "version").GetInt32();
        var newPassword = "CINewPassword!2026";

        var firstUpdate = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = ApiTest.Property(dealer, "firstName").GetString(),
            lastName = ApiTest.Property(dealer, "lastName").GetString(),
            email = ApiTest.Property(dealer, "email").GetString(),
            phone = ApiTest.Property(dealer, "phone").GetString(),
            company = ApiTest.Property(dealer, "company").GetString(),
            isActive = true,
            newPassword,
            version
        }, adminToken);
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = ApiTest.Property(dealer, "firstName").GetString(),
            lastName = ApiTest.Property(dealer, "lastName").GetString(),
            email = ApiTest.Property(dealer, "email").GetString(),
            phone = ApiTest.Property(dealer, "phone").GetString(),
            company = "Stale Admin Form",
            isActive = true,
            newPassword = (string?)null,
            version
        }, adminToken);

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var conflict = await staleUpdate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("USER_CHANGED", ApiTest.Property(conflict, "code").GetString());

        using var relogin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var loginToken = await ApiTest.GetCsrf(relogin);
        var dealerLogin = await ApiTest.SendJson(relogin, HttpMethod.Post, "/api/auth/login",
            new { email = dealerEmail, password = newPassword }, loginToken);
        Assert.Equal(HttpStatusCode.OK, dealerLogin.StatusCode);
    }
}

internal static class ApiTest
{
    public static async Task ReleaseSessionLock(
        SqlConnection connection,
        string resource,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_releaseapplock
                @Resource = @resource,
                @LockOwner = 'Session';
            SELECT @result;
            """;
        command.Parameters.AddWithValue("@resource", resource);
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) >= 0);
    }

    public static object ProductPayload(
        string? code = null,
        int stock = 5,
        decimal price = 100m,
        string? version = null) =>
        new
        {
            code = code ?? "CI-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            name = "CI test ürünü",
            description = "Aranabilir açıklama",
            brand = "CI",
            manufacturerCode = "CI-MAKER",
            specialCode1 = "CI-SPECIAL",
            specialCode2 = "",
            imageUrl = "/images/product.svg",
            stock,
            criticalStock = 2,
            price,
            categoryId = 1,
            version
        };

    public static async Task<string> LoginAdmin(HttpClient client)
    {
        var csrf = await GetCsrf(client);
        var login = await SendJson(client, HttpMethod.Post, "/api/auth/login",
            new { email = "admin@u1.local", password = "U1Admin!2026" }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return await GetCsrf(client);
    }

    public static async Task<string> RegisterDealer(HttpClient client, string? email = null)
    {
        var csrf = await GetCsrf(client);
        var register = await SendJson(client, HttpMethod.Post, "/api/auth/register", new
        {
            firstName = "CI",
            lastName = "Dealer",
            email = email ?? $"ci-{Guid.NewGuid():N}@example.test",
            phone = "05321234567",
            company = "CI Dealer",
            password = "CITest!2026"
        }, csrf);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        return await GetCsrf(client);
    }

    public static JsonElement Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new KeyNotFoundException($"JSON property '{name}' was not found.");
    }

    public static async Task<string> GetCsrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/csrf", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return Property(body, "token").GetString()!;
    }

    public static Task<HttpResponseMessage> SendJson(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string? csrf)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        if (csrf is not null)
            request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
