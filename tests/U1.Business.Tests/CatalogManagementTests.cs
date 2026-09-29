using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class CatalogManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Categories_archive_cart_checkout_restore_and_history_are_consistent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminToken = await ApiTest.LoginAdmin(admin);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceName = "CI Source " + suffix;
        var targetName = "CI Target " + suffix;

        var sourceCreate = await ApiTest.SendJson(
            admin, HttpMethod.Post, "/api/admin/categories",
            new { name = sourceName }, adminToken);
        Assert.Equal(HttpStatusCode.OK, sourceCreate.StatusCode);
        var sourceId = ApiTest.Property(
            await sourceCreate.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
            "id").GetInt32();

        var targetCreate = await ApiTest.SendJson(
            admin, HttpMethod.Post, "/api/admin/categories",
            new { name = targetName }, adminToken);
        Assert.Equal(HttpStatusCode.OK, targetCreate.StatusCode);
        var targetId = ApiTest.Property(
            await targetCreate.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
            "id").GetInt32();

        var categories = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/categories", cancellationToken);
        var source = FindCategory(categories, sourceId);
        var sourceVersion = ApiTest.Property(source, "rowVersion").GetString()!;

        var renamed = await ApiTest.SendJson(
            admin, HttpMethod.Put, $"/api/admin/categories/{sourceId}",
            new { name = sourceName + " Renamed", rowVersion = sourceVersion }, adminToken);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var staleRename = await ApiTest.SendJson(
            admin, HttpMethod.Put, $"/api/admin/categories/{sourceId}",
            new { name = sourceName + " Stale", rowVersion = sourceVersion }, adminToken);
        Assert.Equal(HttpStatusCode.Conflict, staleRename.StatusCode);
        Assert.Equal(
            "CATEGORY_CHANGED",
            ApiTest.Property(
                await staleRename.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
                "code").GetString());

        var productCode = "R10-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var productCreate = await ApiTest.SendJson(
            admin, HttpMethod.Post, "/api/admin/products",
            ProductPayload(productCode, sourceId), adminToken);
        Assert.Equal(HttpStatusCode.OK, productCreate.StatusCode);
        var productId = ApiTest.Property(
            await productCreate.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
            "id").GetInt32();

        using var dealer = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var dealerToken = await ApiTest.RegisterDealer(dealer);

        var forbiddenCategories = await dealer.GetAsync(
            "/api/admin/categories", cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCategories.StatusCode);

        var firstAdd = await ApiTest.SendJson(
            dealer, HttpMethod.Post, "/api/cart",
            new { productId, quantity = 1 }, dealerToken);
        Assert.Equal(HttpStatusCode.OK, firstAdd.StatusCode);

        var firstOrderResponse = await ApiTest.SendJson(
            dealer, HttpMethod.Post, "/api/orders",
            new
            {
                requestId = Guid.NewGuid(),
                note = "R10 historical snapshot",
                lines = new[] { new { productId, quantity = 1, unitPrice = 100m } }
            },
            dealerToken);
        Assert.Equal(HttpStatusCode.OK, firstOrderResponse.StatusCode);
        var firstOrder = await firstOrderResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var firstOrderId = ApiTest.Property(firstOrder, "id").GetInt32();

        var secondAdd = await ApiTest.SendJson(
            dealer, HttpMethod.Post, "/api/cart",
            new { productId, quantity = 1 }, dealerToken);
        Assert.Equal(HttpStatusCode.OK, secondAdd.StatusCode);

        var adminProduct = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/products/{productId}", cancellationToken);
        var archiveVersion = ApiTest.Property(adminProduct, "rowVersion").GetString()!;

        var archive = await ApiTest.SendJson(
            admin, HttpMethod.Post, $"/api/admin/products/{productId}/archive",
            new { rowVersion = archiveVersion, reason = "R10 satıştan kaldırma testi" },
            adminToken);
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var staleArchive = await ApiTest.SendJson(
            admin, HttpMethod.Post, $"/api/admin/products/{productId}/archive",
            new { rowVersion = archiveVersion, reason = "stale" },
            adminToken);
        Assert.Equal(HttpStatusCode.Conflict, staleArchive.StatusCode);
        Assert.Equal(
            "PRODUCT_CHANGED",
            ApiTest.Property(
                await staleArchive.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
                "code").GetString());

        var catalog = await dealer.GetFromJsonAsync<JsonElement>(
            "/api/products?q=" + Uri.EscapeDataString(productCode), cancellationToken);
        Assert.Empty(ApiTest.Property(catalog, "items").EnumerateArray());

        var meta = await dealer.GetFromJsonAsync<JsonElement>(
            "/api/catalog/meta", cancellationToken);
        var sourceMeta = ApiTest.Property(meta, "categories").EnumerateArray()
            .Single(x => ApiTest.Property(x, "id").GetInt32() == sourceId);
        Assert.Equal(0, ApiTest.Property(sourceMeta, "productCount").GetInt32());
        Assert.DoesNotContain(
            ApiTest.Property(meta, "brands").EnumerateArray(),
            brand => brand.GetString() == "CI-R10");

        var cart = await dealer.GetFromJsonAsync<JsonElement>(
            "/api/cart", cancellationToken);
        var cartItem = ApiTest.Property(cart, "items").EnumerateArray().Single();
        Assert.True(ApiTest.Property(cartItem, "isArchived").GetBoolean());
        Assert.Contains(
            "satışta değil",
            ApiTest.Property(cartItem, "unavailableMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);

        var archivedCheckout = await ApiTest.SendJson(
            dealer, HttpMethod.Post, "/api/orders",
            new
            {
                requestId = Guid.NewGuid(),
                note = "R10 archived rejection",
                lines = new[] { new { productId, quantity = 1, unitPrice = 100m } }
            },
            dealerToken);
        Assert.Equal(HttpStatusCode.Conflict, archivedCheckout.StatusCode);
        Assert.Equal(
            "PRODUCT_ARCHIVED",
            ApiTest.Property(
                await archivedCheckout.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
                "code").GetString());

        var historicalOrder = await dealer.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{firstOrderId}", cancellationToken);
        var historicalLine = ApiTest.Property(historicalOrder, "items").EnumerateArray().Single();
        Assert.Equal(productCode, ApiTest.Property(historicalLine, "productCode").GetString());
        Assert.Equal("R10 test ürünü", ApiTest.Property(historicalLine, "productName").GetString());
        Assert.Equal(100m, ApiTest.Property(historicalLine, "unitPrice").GetDecimal());

        var removeArchived = await ApiTest.SendJson(
            dealer, HttpMethod.Delete, $"/api/cart/{productId}", body: null, csrf: dealerToken);
        Assert.Equal(HttpStatusCode.OK, removeArchived.StatusCode);

        categories = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/categories", cancellationToken);
        source = FindCategory(categories, sourceId);
        var target = FindCategory(categories, targetId);
        var merge = await ApiTest.SendJson(
            admin, HttpMethod.Post, $"/api/admin/categories/{sourceId}/merge",
            new
            {
                targetCategoryId = targetId,
                sourceRowVersion = ApiTest.Property(source, "rowVersion").GetString(),
                targetRowVersion = ApiTest.Property(target, "rowVersion").GetString()
            },
            adminToken);
        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        Assert.Equal(
            1,
            ApiTest.Property(
                await merge.Content.ReadFromJsonAsync<JsonElement>(cancellationToken),
                "movedProducts").GetInt32());

        adminProduct = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/products/{productId}", cancellationToken);
        Assert.Equal(targetId, ApiTest.Property(adminProduct, "categoryId").GetInt32());
        Assert.True(ApiTest.Property(adminProduct, "isArchived").GetBoolean());

        var restore = await ApiTest.SendJson(
            admin, HttpMethod.Post, $"/api/admin/products/{productId}/restore",
            new { rowVersion = ApiTest.Property(adminProduct, "rowVersion").GetString() },
            adminToken);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        catalog = await dealer.GetFromJsonAsync<JsonElement>(
            "/api/products?q=" + Uri.EscapeDataString(productCode), cancellationToken);
        var restoredProduct = ApiTest.Property(catalog, "items").EnumerateArray().Single();
        Assert.Equal(targetId, ApiTest.Property(restoredProduct, "categoryId").GetInt32());

        var productHistory = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/admin/history?productId={productId}", cancellationToken);
        var productHistoryItems = ApiTest.Property(productHistory, "items").EnumerateArray().ToList();
        Assert.Contains(productHistoryItems, item =>
            ApiTest.Property(item, "action").GetString() == "ProductArchived");
        Assert.Contains(productHistoryItems, item =>
            ApiTest.Property(item, "action").GetString() == "ProductRestored");

        var categoryHistory = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/history?action=CategoryMerged", cancellationToken);
        Assert.Contains(
            ApiTest.Property(categoryHistory, "items").EnumerateArray(),
            item => ApiTest.Property(item, "summary").GetString()!.Contains(
                targetName, StringComparison.Ordinal));
    }

    private static JsonElement FindCategory(JsonElement response, int id) =>
        response.EnumerateArray().Single(
            item => ApiTest.Property(item, "id").GetInt32() == id);

    private static object ProductPayload(string code, int categoryId) =>
        new
        {
            code,
            name = "R10 test ürünü",
            description = "R10 kategori ve arşiv testi",
            brand = "CI-R10",
            manufacturerCode = "R10-MAKER",
            specialCode1 = "R10-SPECIAL",
            specialCode2 = "",
            imageUrl = "/images/product.svg",
            stock = 5,
            criticalStock = 2,
            price = 100m,
            categoryId
        };
}
