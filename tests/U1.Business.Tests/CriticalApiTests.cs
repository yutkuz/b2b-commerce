using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace U1.Business.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string DatabaseName = "U1Business_CI";
    public const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=U1Business_CI;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=30";

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
                ["ConnectionStrings:SqlServer"] = ConnectionString
            });
        });
    }

    public async Task ResetCheckoutState()
    {
        using var db = new SqlConnection(ConnectionString);
        await db.OpenAsync();
        await db.ExecuteAsync("""
            DELETE FROM OrderItems;
            DELETE FROM Orders;
            DELETE FROM CartItems WHERE CartId IN (
                SELECT c.Id FROM Carts c JOIN Users u ON u.Id=c.UserId WHERE u.Email LIKE 'ci-%@example.test'
            );
            DELETE FROM Carts WHERE UserId IN (SELECT Id FROM Users WHERE Email LIKE 'ci-%@example.test');
            DELETE FROM Users WHERE Email LIKE 'ci-%@example.test';
            UPDATE Products SET Stock=24 WHERE Code='DG-001';
            """);
    }
}

public sealed class CriticalApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Eight_concurrent_dealers_can_checkout_without_server_errors()
    {
        await factory.ResetCheckoutState();

        var clients = Enumerable.Range(0, 8)
            .Select(_ => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true }))
            .ToArray();

        var sessionTokens = new string[clients.Length];
        for (var i = 0; i < clients.Length; i++)
        {
            var anonymousToken = await GetCsrf(clients[i]);
            var email = $"ci-{Guid.NewGuid():N}@example.test";
            var register = await SendJson(clients[i], HttpMethod.Post, "/api/auth/register", new
            {
                firstName = "CI",
                lastName = $"Dealer{i}",
                email,
                phone = $"05321234{i + 100}",
                company = $"CI Dealer {i}",
                password = "CITest!2026"
            }, anonymousToken);
            Assert.Equal(HttpStatusCode.OK, register.StatusCode);
            sessionTokens[i] = await GetCsrf(clients[i]);
        }

        var products = await clients[0].GetFromJsonAsync<JsonElement>("/api/products?q=DG-001");
        var product = products.GetProperty("items")[0];
        var productId = product.GetProperty("id").GetInt32();
        var price = product.GetProperty("price").GetDecimal();

        for (var i = 0; i < clients.Length; i++)
        {
            var cart = await SendJson(clients[i], HttpMethod.Post, "/api/cart",
                new { productId, quantity = 1 }, sessionTokens[i]);
            Assert.Equal(HttpStatusCode.OK, cart.StatusCode);
        }

        var requests = clients.Select((client, i) =>
            SendJson(client, HttpMethod.Post, "/api/orders", new
            {
                requestId = Guid.NewGuid(),
                note = "CI concurrent checkout",
                lines = new[] { new { productId, quantity = 1, unitPrice = price } }
            }, sessionTokens[i]));

        var responses = await Task.WhenAll(requests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var refreshed = await clients[0].GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
        Assert.Equal(16, refreshed.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Stale_admin_profile_update_cannot_revert_a_new_password()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var anonymousToken = await GetCsrf(admin);
        var login = await SendJson(admin, HttpMethod.Post, "/api/auth/login",
            new { email = "admin@u1.local", password = "U1Admin!2026" }, anonymousToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var adminToken = await GetCsrf(admin);
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/admin/users?q=bayi%40u1.local");
        var dealer = users.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("email").GetString() == "bayi@u1.local");

        var id = dealer.GetProperty("id").GetInt32();
        var version = dealer.GetProperty("version").GetInt32();
        var newPassword = "CINewPassword!2026";

        var firstUpdate = await SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = dealer.GetProperty("firstName").GetString(),
            lastName = dealer.GetProperty("lastName").GetString(),
            email = dealer.GetProperty("email").GetString(),
            phone = dealer.GetProperty("phone").GetString(),
            company = dealer.GetProperty("company").GetString(),
            isActive = true,
            newPassword,
            version
        }, adminToken);
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = dealer.GetProperty("firstName").GetString(),
            lastName = dealer.GetProperty("lastName").GetString(),
            email = dealer.GetProperty("email").GetString(),
            phone = dealer.GetProperty("phone").GetString(),
            company = "Stale Admin Form",
            isActive = true,
            newPassword = (string?)null,
            version
        }, adminToken);

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var conflict = await staleUpdate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("USER_CHANGED", conflict.GetProperty("code").GetString());

        using var dealerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var dealerAnonymousToken = await GetCsrf(dealerClient);
        var dealerLogin = await SendJson(dealerClient, HttpMethod.Post, "/api/auth/login",
            new { email = "bayi@u1.local", password = newPassword }, dealerAnonymousToken);
        Assert.Equal(HttpStatusCode.OK, dealerLogin.StatusCode);
    }

    private static async Task<string> GetCsrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> SendJson(
        HttpClient client, HttpMethod method, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return client.SendAsync(request);
    }
}
