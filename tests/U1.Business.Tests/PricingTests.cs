using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class PricingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Dealer_group_pricing_is_server_authoritative_rounded_and_snapshotted()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var groupName = "R20 " + suffix;
        using var groupCreate = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/dealer-groups",
            new { name = groupName, discountPercent = 50m }, adminCsrf);
        Assert.Equal(HttpStatusCode.OK, groupCreate.StatusCode);
        var groupId = ApiTest.Property(
            await groupCreate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken),
            "id").GetInt32();

        var code = "R20-" + suffix.ToUpperInvariant();
        using var productCreate = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products", new
        {
            code,
            name = "R20 pricing product",
            description = "Pricing test",
            brand = "CI",
            manufacturerCode = "R20",
            imageUrl = "/images/product.svg",
            stock = 4,
            criticalStock = 1,
            price = 10.05m,
            categoryId = 1
        }, adminCsrf);
        productCreate.EnsureSuccessStatusCode();
        var productId = ApiTest.Property(
            await productCreate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken),
            "id").GetInt32();

        var dealerEmail = $"ci-r20-{suffix}@example.test";
        using (var registering = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }))
            await ApiTest.RegisterDealer(registering, dealerEmail);

        var users = await admin.GetFromJsonAsync<JsonElement>(
            "/api/admin/users?q=" + Uri.EscapeDataString(dealerEmail),
            TestContext.Current.CancellationToken);
        var dealer = ApiTest.Property(users, "items")[0];
        using var assign = await ApiTest.SendJson(admin, HttpMethod.Put,
            $"/api/admin/users/{ApiTest.Property(dealer, "id").GetInt32()}", new
            {
                firstName = ApiTest.Property(dealer, "firstName").GetString(),
                lastName = ApiTest.Property(dealer, "lastName").GetString(),
                email = dealerEmail,
                phone = ApiTest.Property(dealer, "phone").GetString(),
                company = ApiTest.Property(dealer, "company").GetString(),
                isActive = true,
                dealerGroupId = groupId,
                version = ApiTest.Property(dealer, "version").GetInt32()
            }, adminCsrf);
        assign.EnsureSuccessStatusCode();

        using var dealerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var dealerCsrf = await LoginDealer(dealerClient, dealerEmail);
        var product = await FindProduct(dealerClient, code);
        Assert.Equal(10.05m, ApiTest.Property(product, "listPrice").GetDecimal());
        Assert.Equal(5.03m, ApiTest.Property(product, "price").GetDecimal());
        Assert.Equal(50m, ApiTest.Property(product, "discountPercent").GetDecimal());

        using var add = await ApiTest.SendJson(dealerClient, HttpMethod.Post, "/api/cart",
            new { productId, quantity = 1 }, dealerCsrf);
        add.EnsureSuccessStatusCode();
        var cart = await dealerClient.GetFromJsonAsync<JsonElement>("/api/cart", TestContext.Current.CancellationToken);
        Assert.Equal(5.03m, ApiTest.Property(ApiTest.Property(cart, "items")[0], "price").GetDecimal());

        var groups = await admin.GetFromJsonAsync<JsonElement>("/api/admin/dealer-groups", TestContext.Current.CancellationToken);
        var group = ApiTest.Property(groups, "items").EnumerateArray()
            .Single(x => ApiTest.Property(x, "id").GetInt32() == groupId);
        using var groupUpdate = await ApiTest.SendJson(admin, HttpMethod.Put,
            $"/api/admin/dealer-groups/{groupId}", new
            {
                name = groupName,
                discountPercent = 40m,
                rowVersion = ApiTest.Property(group, "rowVersion").GetString()
            }, adminCsrf);
        groupUpdate.EnsureSuccessStatusCode();

        var requestId = Guid.NewGuid();
        using var staleCheckout = await ApiTest.SendJson(dealerClient, HttpMethod.Post, "/api/orders", new
        {
            requestId,
            note = "R20 stale price",
            lines = new[] { new { productId, quantity = 1, unitPrice = 5.03m } }
        }, dealerCsrf);
        Assert.Equal(HttpStatusCode.Conflict, staleCheckout.StatusCode);
        var staleBody = await staleCheckout.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("CART_CHANGED", ApiTest.Property(staleBody, "code").GetString());

        cart = await dealerClient.GetFromJsonAsync<JsonElement>("/api/cart", TestContext.Current.CancellationToken);
        Assert.Equal(6.03m, ApiTest.Property(ApiTest.Property(cart, "items")[0], "price").GetDecimal());
        using var checkout = await ApiTest.SendJson(dealerClient, HttpMethod.Post, "/api/orders", new
        {
            requestId,
            note = "R20 current price",
            lines = new[] { new { productId, quantity = 1, unitPrice = 6.03m } }
        }, dealerCsrf);
        checkout.EnsureSuccessStatusCode();
        var orderId = ApiTest.Property(
            await checkout.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken),
            "id").GetInt32();

        var orderDetail = await dealerClient.GetFromJsonAsync<JsonElement>(
            $"/api/orders/{orderId}", TestContext.Current.CancellationToken);
        var line = ApiTest.Property(orderDetail, "items")[0];
        Assert.Equal(10.05m, ApiTest.Property(line, "listUnitPrice").GetDecimal());
        Assert.Equal(6.03m, ApiTest.Property(line, "unitPrice").GetDecimal());
        Assert.Equal(40m, ApiTest.Property(line, "discountPercent").GetDecimal());

        using var secondDealer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await ApiTest.RegisterDealer(secondDealer);
        var otherProduct = await FindProduct(secondDealer, code);
        Assert.Equal(10.05m, ApiTest.Property(otherProduct, "price").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.True(await db.AdminEvents.AnyAsync(x =>
            x.EventType == "DealerGroupAssigned" && x.EntityId == ApiTest.Property(dealer, "id").GetInt32(),
            TestContext.Current.CancellationToken));
        Assert.True(await db.AdminEvents.AnyAsync(x =>
            x.EventType == "DealerGroupUpdated" && x.EntityId == groupId,
            TestContext.Current.CancellationToken));
    }

    private static async Task<string> LoginDealer(HttpClient client, string email)
    {
        var csrf = await ApiTest.GetCsrf(client);
        using var login = await ApiTest.SendJson(client, HttpMethod.Post, "/api/auth/login",
            new { email, password = "CITest!2026" }, csrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return await ApiTest.GetCsrf(client);
    }

    private static async Task<JsonElement> FindProduct(HttpClient client, string code)
    {
        var body = await client.GetFromJsonAsync<JsonElement>(
            "/api/products?q=" + Uri.EscapeDataString(code),
            TestContext.Current.CancellationToken);
        return ApiTest.Property(body, "items").EnumerateArray()
            .Single(x => ApiTest.Property(x, "code").GetString() == code);
    }
}
