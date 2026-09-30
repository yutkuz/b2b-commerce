using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class ArchiveCheckoutRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Archive_and_checkout_follow_the_committed_lock_order(bool archiveFirst)
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var buyer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        var email = $"ci-{Guid.NewGuid():N}@example.test";
        var buyerCsrf = await ApiTest.RegisterDealer(buyer, email);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        int productId;
        int buyerId;
        await using (var db = await NewDb(token))
        {
            var categoryId = await db.Categories.Select(x => x.Id).FirstAsync(token);
            buyerId = await db.Users.Where(x => x.Email == email).Select(x => x.Id).SingleAsync(token);
            var product = new Product
            {
                Code = "R10-RACE-" + suffix.ToUpperInvariant(),
                Name = "R10 yarış ürünü",
                Description = "CI",
                Brand = "CI",
                ManufacturerCode = "CI",
                ImageUrl = "/images/product.svg",
                Stock = 4,
                CriticalStock = 1,
                Price = 100m,
                CategoryId = categoryId
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(token);
            productId = product.Id;
        }

        using var add = await ApiTest.SendJson(buyer, HttpMethod.Post,
            "/api/cart", new { productId, quantity = 1 }, buyerCsrf);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var productInfo = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/products/{productId}", token);
        var version = ApiTest.Property(productInfo, "rowVersion").GetString()!;
        var requestId = Guid.NewGuid();

        Task<HttpResponseMessage> Checkout() => ApiTest.SendJson(buyer, HttpMethod.Post,
            "/api/orders", new
            {
                requestId,
                note = "R10 race",
                lines = new[] { new { productId, quantity = 1, unitPrice = 100m } }
            }, buyerCsrf);
        Task<HttpResponseMessage> Archive() => ApiTest.SendJson(admin, HttpMethod.Post,
            $"/api/admin/products/{productId}/archive",
            new { rowVersion = version, reason = "R10 yarış" }, adminCsrf);

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(token);
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.Serializable, token);
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = (SqlTransaction)blockerTx;
            command.CommandText = archiveFirst
                ? "SELECT * FROM Carts WITH(XLOCK,HOLDLOCK) WHERE UserId=@id"
                : "SELECT * FROM Products WITH(XLOCK,HOLDLOCK) WHERE Id=@id";
            command.Parameters.AddWithValue("@id", archiveFirst ? buyerId : productId);
            using var reader = await command.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
        }

        Task<HttpResponseMessage>? checkoutTask = null;
        Task<HttpResponseMessage>? archiveTask = null;
        Exception? waitFailure = null;
        try
        {
            checkoutTask = Checkout();
            await WaitForBlockedRequests(1, token);
            archiveTask = Archive();
            if (!archiveFirst)
                await WaitForBlockedRequests(2, token);
            else
            {
                var archiveResponse = await archiveTask;
                Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            waitFailure = ex;
        }
        finally
        {
            await blockerTx.CommitAsync(CancellationToken.None);
        }

        using var checkout = await checkoutTask!;
        using var archive = await archiveTask!;
        if (waitFailure is not null)
            throw waitFailure;

        Assert.Equal(archiveFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK, checkout.StatusCode);
        Assert.True(archive.StatusCode == (archiveFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict),
            $"Archive returned {(int)archive.StatusCode}: {await archive.Content.ReadAsStringAsync(token)}");
        if (archiveFirst)
            Assert.Equal("PRODUCT_ARCHIVED", ApiTest.Property(
                await checkout.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());
        else
            Assert.Equal("PRODUCT_CHANGED", ApiTest.Property(
                await archive.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        await using var check = await NewDb(token);
        var productAfter = await check.Products.SingleAsync(x => x.Id == productId, token);
        Assert.Equal(archiveFirst, productAfter.IsArchived);
        Assert.Equal(archiveFirst ? 4 : 3, productAfter.Stock);
        Assert.Equal(archiveFirst ? 0 : 1,
            await check.Orders.CountAsync(x => x.RequestId == requestId, token));
        Assert.Equal(archiveFirst ? 1 : 0,
            await check.AdminEvents.CountAsync(x => x.EventType == "ProductArchived" && x.EntityId == productId, token));
        Assert.Equal(archiveFirst ? 0 : 1,
            await check.StockMovements.CountAsync(x => x.ProductId == productId && x.MovementType == "OrderPlaced", token));
        var cartCount = await check.CartItems.CountAsync(x => x.ProductId == productId, token);
        Assert.Equal(archiveFirst ? 1 : 0, cartCount);
        if (!archiveFirst)
        {
            var item = await check.OrderItems.SingleAsync(x => x.ProductId == productId, token);
            Assert.Equal("R10 yarış ürünü", item.ProductName);
            Assert.Equal(100m, item.UnitPrice);
        }
    }

    private Task<BusinessDbContext> NewDb(CancellationToken token) =>
        factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(token);

    private static async Task WaitForBlockedRequests(int expected, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        await using var probe = new SqlConnection(ApiFactory.ConnectionString);
        await probe.OpenAsync(token);
        using var command = probe.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND wait_type LIKE 'LCK_M_%'";
        while (DateTime.UtcNow < deadline)
        {
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) >= expected)
                return;
            await Task.Delay(25, token);
        }

        throw new TimeoutException($"{expected} işlem SQL kilidinde beklemedi.");
    }
}
