using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class OrderOperationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Status_history_reasons_private_note_and_terminal_rules_are_consistent()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var buyer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        var buyerCsrf = await ApiTest.RegisterDealer(buyer);
        var productId = await CreateProduct(token);
        var orderId = await Checkout(buyer, buyerCsrf, productId, 1, 100m, token);
        var orderDetail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        var originalVersion = OrderVersion(orderDetail);
        Assert.Single(ApiTest.Property(orderDetail, "history").EnumerateArray());
        Assert.False(ApiTest.Property(orderDetail, "order").TryGetProperty("adminNote", out _));

        using var note = await ApiTest.SendJson(admin, HttpMethod.Put,
            $"/api/admin/orders/{orderId}/note",
            new { note = "Yalnız yönetici görür", rowVersion = originalVersion }, adminCsrf);
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
        var internalNote = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/orders/{orderId}/internal", token);
        Assert.Equal("Yalnız yönetici görür", ApiTest.Property(internalNote, "adminNote").GetString());
        using var forbiddenInternal = await buyer.GetAsync($"/api/admin/orders/{orderId}/internal", token);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenInternal.StatusCode);
        orderDetail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        Assert.DoesNotContain("Yalnız yönetici görür", orderDetail.ToString());

        using var stale = await ChangeStatus(admin, adminCsrf, orderId,
            "Onaylandı", null, originalVersion);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("ORDER_CHANGED", ApiTest.Property(
            await stale.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        using var invalid = await ChangeStatus(admin, adminCsrf, orderId,
            "Sevk edildi", null, OrderVersion(orderDetail));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal("ORDER_STATUS_INVALID", ApiTest.Property(
            await invalid.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        using var missingReason = await ChangeStatus(admin, adminCsrf, orderId,
            "Reddedildi", "", OrderVersion(orderDetail));
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        foreach (var status in new[] { "Onaylandı", "Hazırlanıyor", "Sevk edildi", "Teslim edildi" })
        {
            orderDetail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
            using var response = await ChangeStatus(admin, adminCsrf, orderId,
                status, null, OrderVersion(orderDetail));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (status == "Sevk edildi")
            {
                var shipped = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
                using var shippedReject = await ChangeStatus(admin, adminCsrf, orderId,
                    "Reddedildi", "Sevk sonrası olmaz", OrderVersion(shipped));
                Assert.Equal(HttpStatusCode.Conflict, shippedReject.StatusCode);
                Assert.Equal("ORDER_STATUS_INVALID", ApiTest.Property(
                    await shippedReject.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());
            }
        }

        orderDetail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        Assert.Equal("Teslim edildi", ApiTest.Property(ApiTest.Property(orderDetail, "order"), "status").GetString());
        Assert.Equal(5, ApiTest.Property(orderDetail, "history").GetArrayLength());
        using var lateReject = await ChangeStatus(admin, adminCsrf, orderId,
            "Reddedildi", "Geç kaldı", OrderVersion(orderDetail));
        Assert.Equal(HttpStatusCode.Conflict, lateReject.StatusCode);
        using var repeat = await ChangeStatus(admin, adminCsrf, orderId,
            "Teslim edildi", null, originalVersion);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        await using var check = await NewDb(token);
        Assert.Equal(3, await check.Products.Where(x => x.Id == productId)
            .Select(x => x.Stock).SingleAsync(token));
        Assert.Equal(1, await check.StockMovements.CountAsync(x =>
            x.ProductId == productId && x.MovementType == "OrderPlaced", token));
        Assert.Equal(0, await check.StockMovements.CountAsync(x =>
            x.ProductId == productId && x.MovementType == "OrderRejected", token));
        Assert.Equal(1, await check.AdminEvents.CountAsync(x =>
            x.EntityType == "Order" && x.EntityId == orderId && x.EventType == "OrderAdminNoteChanged", token));
    }

    [Fact]
    public async Task Rejection_returns_stock_once_and_readd_requires_current_price_and_availability()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var buyer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        var buyerCsrf = await ApiTest.RegisterDealer(buyer);
        var productId = await CreateProduct(token);
        var orderId = await Checkout(buyer, buyerCsrf, productId, 1, 100m, token);
        foreach (var status in new[] { "Onaylandı", "Hazırlanıyor", "Reddedildi" })
        {
            var detail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
            using var response = await ChangeStatus(admin, adminCsrf, orderId,
                status, status == "Reddedildi" ? "Ürün uygun değil" : null,
                OrderVersion(detail));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var rejected = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        Assert.Equal("Ürün uygun değil", ApiTest.Property(
            ApiTest.Property(rejected, "order"), "rejectionReason").GetString());
        using var repeatReject = await ChangeStatus(admin, adminCsrf, orderId,
            "Reddedildi", "Ürün uygun değil", OrderVersion(rejected));
        Assert.Equal(HttpStatusCode.OK, repeatReject.StatusCode);
        await using (var db = await NewDb(token))
        {
            Assert.Equal(4, await db.Products.Where(x => x.Id == productId)
                .Select(x => x.Stock).SingleAsync(token));
            Assert.Equal(1, await db.StockMovements.CountAsync(x =>
                x.ProductId == productId && x.MovementType == "OrderRejected", token));
            var product = await db.Products.SingleAsync(x => x.Id == productId, token);
            product.Price = 125m;
            await db.SaveChangesAsync(token);
        }

        var preview = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{orderId}/readd-preview", token);
        var previewLine = ApiTest.Property(preview, "items")[0];
        Assert.Equal(100m, ApiTest.Property(previewLine, "previousUnitPrice").GetDecimal());
        Assert.Equal(125m, ApiTest.Property(previewLine, "currentUnitPrice").GetDecimal());
        using var oldPrice = await ApiTest.SendJson(buyer, HttpMethod.Post,
            $"/api/orders/{orderId}/readd",
            new { lines = new[] { new { productId, quantity = 1, unitPrice = 100m } } }, buyerCsrf);
        Assert.Equal(HttpStatusCode.Conflict, oldPrice.StatusCode);
        Assert.Equal("PRICE_CHANGED", ApiTest.Property(
            await oldPrice.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        using var readd = await ApiTest.SendJson(buyer, HttpMethod.Post,
            $"/api/orders/{orderId}/readd",
            new { lines = new[] { new { productId, quantity = 1, unitPrice = 125m } } }, buyerCsrf);
        Assert.Equal(HttpStatusCode.OK, readd.StatusCode);
        var cart = await buyer.GetFromJsonAsync<JsonElement>("/api/cart", token);
        Assert.Equal(1, ApiTest.Property(cart, "count").GetInt32());
        Assert.Equal(125m, ApiTest.Property(cart, "total").GetDecimal());
        await using var check = await NewDb(token);
        Assert.Equal(1, await check.Orders.CountAsync(x => x.Id == orderId, token));
        Assert.Equal(100m, await check.OrderItems.Where(x => x.OrderId == orderId)
            .Select(x => x.UnitPrice).SingleAsync(token));
    }

    [Fact]
    public async Task Cancellation_and_archived_readd_do_not_create_an_order_or_duplicate_stock()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var buyer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var other = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        var buyerCsrf = await ApiTest.RegisterDealer(buyer);
        var otherCsrf = await ApiTest.RegisterDealer(other);
        var productId = await CreateProduct(token);
        var orderId = await Checkout(buyer, buyerCsrf, productId, 1, 100m, token);
        var detail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        using var cancel = await ChangeStatus(admin, adminCsrf, orderId,
            "İptal edildi", "Bayi talebi", OrderVersion(detail));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        detail = await buyer.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}", token);
        Assert.Equal("Bayi talebi", ApiTest.Property(
            ApiTest.Property(detail, "order"), "rejectionReason").GetString());
        using var repeat = await ChangeStatus(admin, adminCsrf, orderId,
            "İptal edildi", "Bayi talebi", OrderVersion(detail));
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        var product = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/products/{productId}", token);
        using var archive = await ApiTest.SendJson(admin, HttpMethod.Post,
            $"/api/admin/products/{productId}/archive",
            new { rowVersion = ApiTest.Property(product, "rowVersion").GetString(), reason = "R13 test" },
            adminCsrf);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        var preview = await buyer.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{orderId}/readd-preview", token);
        Assert.False(ApiTest.Property(ApiTest.Property(preview, "items")[0], "canAdd").GetBoolean());
        using var readd = await ApiTest.SendJson(buyer, HttpMethod.Post,
            $"/api/orders/{orderId}/readd",
            new { lines = new[] { new { productId, quantity = 1, unitPrice = 100m } } }, buyerCsrf);
        Assert.Equal(HttpStatusCode.Conflict, readd.StatusCode);
        Assert.Equal("PRODUCT_ARCHIVED", ApiTest.Property(
            await readd.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        using var forbidden = await other.GetAsync($"/api/orders/{orderId}/readd-preview", token);
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        using var forbiddenWrite = await ApiTest.SendJson(other, HttpMethod.Post,
            $"/api/orders/{orderId}/readd",
            new { lines = new[] { new { productId, quantity = 1, unitPrice = 100m } } }, otherCsrf);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenWrite.StatusCode);
        await using var check = await NewDb(token);
        Assert.Equal(4, await check.Products.Where(x => x.Id == productId)
            .Select(x => x.Stock).SingleAsync(token));
        Assert.Equal(1, await check.StockMovements.CountAsync(x =>
            x.ProductId == productId && x.MovementType == "OrderCancelled", token));
        Assert.Equal(0, await check.CartItems.CountAsync(x => x.ProductId == productId, token));
        Assert.Equal(1, await check.Orders.CountAsync(x => x.Id == orderId, token));
    }

    private async Task<int> CreateProduct(CancellationToken token)
    {
        await using var db = await NewDb(token);
        var categoryId = await db.Categories.Select(x => x.Id).FirstAsync(token);
        var product = new Product
        {
            Code = "R13-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Name = "R13 ürün",
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
        return product.Id;
    }

    private static async Task<int> Checkout(
        HttpClient buyer, string csrf, int productId, int quantity, decimal price, CancellationToken token)
    {
        using var add = await ApiTest.SendJson(buyer, HttpMethod.Post,
            "/api/cart", new { productId, quantity }, csrf);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using var checkout = await ApiTest.SendJson(buyer, HttpMethod.Post, "/api/orders",
            new
            {
                requestId = Guid.NewGuid(),
                note = "R13 test",
                lines = new[] { new { productId, quantity, unitPrice = price } }
            }, csrf);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        return ApiTest.Property(await checkout.Content.ReadFromJsonAsync<JsonElement>(token), "id").GetInt32();
    }

    private static Task<HttpResponseMessage> ChangeStatus(
        HttpClient admin, string csrf, int orderId, string status, string? reason, string version) =>
        ApiTest.SendJson(admin, HttpMethod.Put, $"/api/admin/orders/{orderId}/status",
            new { status, reason, rowVersion = version }, csrf);

    private static string OrderVersion(JsonElement detail) =>
        ApiTest.Property(ApiTest.Property(detail, "order"), "rowVersion").GetString()!;

    private Task<BusinessDbContext> NewDb(CancellationToken token) =>
        factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(token);
}
