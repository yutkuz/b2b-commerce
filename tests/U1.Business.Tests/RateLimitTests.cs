using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class RateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task User_scoped_policies_return_retry_after_without_consuming_another_users_quota()
    {
        using var limitedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimits:CatalogSearch:PermitLimit"] = "2",
                    ["RateLimits:CatalogSearch:WindowSeconds"] = "120",
                    ["RateLimits:CommerceWrite:PermitLimit"] = "2",
                    ["RateLimits:CommerceWrite:WindowSeconds"] = "120",
                    ["RateLimits:ImageUpload:PermitLimit"] = "2",
                    ["RateLimits:ImageUpload:WindowSeconds"] = "120"
                })));

        using var first = limitedFactory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var second = limitedFactory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var firstCsrf = await Register(first, "first");
        var secondCsrf = await Register(second, "second");

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/products", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/products", TestContext.Current.CancellationToken)).StatusCode);
        using var catalogLimited = await first.GetAsync("/api/products", TestContext.Current.CancellationToken);
        await AssertRateLimited(catalogLimited);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/products", TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await DeleteCart(first, firstCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await DeleteCart(first, firstCsrf)).StatusCode);
        using var commerceLimited = await DeleteCart(first, firstCsrf);
        await AssertRateLimited(commerceLimited);
        Assert.Equal(HttpStatusCode.OK, (await DeleteCart(second, secondCsrf)).StatusCode);

        using var admin = limitedFactory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true });
        var adminCsrf = await ApiTest.LoginAdmin(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await InvalidUpload(admin, adminCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await InvalidUpload(admin, adminCsrf)).StatusCode);
        using var uploadLimited = await InvalidUpload(admin, adminCsrf);
        await AssertRateLimited(uploadLimited);
    }

    private static async Task<string> Register(HttpClient client, string marker)
    {
        var csrf = await ApiTest.GetCsrf(client);
        var response = await ApiTest.SendJson(
            client,
            HttpMethod.Post,
            "/api/auth/register",
            new
            {
                firstName = "Rate",
                lastName = "Limit",
                email = $"ci-rate-{marker}-{Guid.NewGuid():N}@example.test",
                phone = "05321234567",
                company = "CI Rate Limit",
                password = "Dealer123!"
            },
            csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTest.GetCsrf(client);
    }

    private static Task<HttpResponseMessage> DeleteCart(HttpClient client, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/cart/1");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> InvalidUpload(HttpClient client, string csrf)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "invalid.png");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/images")
        {
            Content = form
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertRateLimited(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.TryParse(values.Single(), out var retryAfter) && retryAfter > 0);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken);
        Assert.Equal("RATE_LIMITED", ApiTest.Property(body, "code").GetString());
        Assert.True(ApiTest.Property(body, "retryAfterSeconds").GetInt32() > 0);
    }
}
