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
public sealed class DashboardSummaryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Approved_total_survives_forward_status_transitions_and_excludes_pending_rejected_cancelled()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var baseline = await Summary(admin, token);
        var totalBefore = ApiTest.Property(baseline, "revenue").GetDecimal();
        await using var db = await NewDb(token);
        var userId = await db.Users.Where(x => x.Role == "Dealer").Select(x => x.Id).FirstAsync(token);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var orders = new[] { "Bekliyor", "Reddedildi", "İptal edildi", "Onaylandı", "Hazırlanıyor", "Sevk edildi", "Teslim edildi" }
            .Select((status, index) => new Order
            {
                Number = $"R24-{suffix}-{index}", UserId = userId, Status = status,
                Total = 100m, CreatedAt = DateTime.UtcNow, RequestId = Guid.NewGuid()
            }).ToArray();
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync(token);
        Assert.Equal(totalBefore + 400m, ApiTest.Property(await Summary(admin, token), "revenue").GetDecimal());
        var transitioning = orders[0];
        foreach (var status in new[] { "Onaylandı", "Hazırlanıyor", "Sevk edildi", "Teslim edildi" })
        {
            var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/orders/{transitioning.Id}", token);
            using var response = await ApiTest.SendJson(admin, HttpMethod.Put,
                $"/api/admin/orders/{transitioning.Id}/status", new
                {
                    status,
                    rowVersion = ApiTest.Property(ApiTest.Property(detail, "order"), "rowVersion").GetString()
                }, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(totalBefore + 500m, ApiTest.Property(await Summary(admin, token), "revenue").GetDecimal());
        }
    }

    [Fact]
    public async Task Archive_and_restore_update_sale_product_count_and_low_stock_list()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var baseline = ApiTest.Property(await Summary(admin, token), "products").GetInt32();
        await using var db = await NewDb(token);
        var product = new Product
        {
            Code = "R24-" + Guid.NewGuid().ToString("N")[..10], Name = "R24 critical stock",
            CategoryId = await db.Categories.Select(x => x.Id).FirstAsync(token),
            Price = 10m, Stock = 0, CriticalStock = 1, CreatedAt = DateTime.UtcNow
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(token);
        // Keep the test product inside the eight-row dashboard limit without changing existing products.
        var earlierCritical = await db.Products.CountAsync(x => !x.IsArchived && x.Stock == 0 && x.Id < product.Id, token);
        Assert.True(earlierCritical < 8, "Test fixture has more zero-stock products than the dashboard limit.");
        var summary = await Summary(admin, token);
        Assert.Equal(baseline + 1, ApiTest.Property(summary, "products").GetInt32());
        Assert.Contains(ApiTest.Property(summary, "lowStock").EnumerateArray(), x => ApiTest.Property(x, "id").GetInt32() == product.Id);
        foreach (var action in new[] { "archive", "restore" })
        {
            var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/products/{product.Id}", token);
            using var response = await ApiTest.SendJson(admin, HttpMethod.Post,
                $"/api/admin/products/{product.Id}/{action}", new
                {
                    rowVersion = ApiTest.Property(detail, "rowVersion").GetString(), reason = "R24 dashboard test"
                }, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            summary = await Summary(admin, token);
            Assert.Equal(baseline + (action == "restore" ? 1 : 0), ApiTest.Property(summary, "products").GetInt32());
            Assert.Equal(action == "restore", ApiTest.Property(summary, "lowStock").EnumerateArray()
                .Any(x => ApiTest.Property(x, "id").GetInt32() == product.Id));
        }
    }

    private static Task<JsonElement> Summary(HttpClient admin, CancellationToken token) =>
        admin.GetFromJsonAsync<JsonElement>("/api/admin/dashboard", token);

    private Task<BusinessDbContext> NewDb(CancellationToken token) =>
        factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>().CreateDbContextAsync(token);
}
