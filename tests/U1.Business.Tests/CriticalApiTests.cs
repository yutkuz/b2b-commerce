using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using Microsoft.Extensions.Configuration;
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
                ["ConnectionStrings:SqlServer"] = ConnectionString
            });
        });
    }

    public async Task ResetCheckoutState()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();

        await db.OrderItems.ExecuteDeleteAsync();
        await db.Orders.ExecuteDeleteAsync();

        var testUsers = db.Users.Where(u => EF.Functions.Like(u.Email, "ci-%@example.test"));
        var testUserIds = testUsers.Select(u => u.Id);
        var testCarts = db.Carts.Where(c => testUserIds.Contains(c.UserId));
        var testCartIds = testCarts.Select(c => c.Id);

        await db.CartItems.Where(ci => testCartIds.Contains(ci.CartId)).ExecuteDeleteAsync();
        await testCarts.ExecuteDeleteAsync();
        await testUsers.ExecuteDeleteAsync();
        await db.Products
            .Where(p => p.Code == "DG-001")
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Stock, 24));
    }
}

public sealed class CriticalApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Test_database_is_unique_and_isolated_from_the_default_database()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var environment = await client.GetFromJsonAsync<JsonElement>("/api/test-environment");

        Assert.StartsWith("U1Business_CI_", ApiFactory.DatabaseName);
        Assert.NotEqual("U1Business", ApiFactory.DatabaseName);
        Assert.Equal(ApiFactory.DatabaseName, Property(environment, "database").GetString());
    }

    [Fact]
    public async Task Eight_concurrent_dealers_can_checkout_without_server_errors()
    {
        using var bootstrap = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
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
        var product = Property(products, "items")[0];
        var productId = Property(product, "id").GetInt32();
        var price = Property(product, "price").GetDecimal();

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

        var failures = new List<string>();
        foreach (var response in responses)
            if (response.StatusCode != HttpStatusCode.OK)
                failures.Add($"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.True(failures.Count == 0, "Concurrent checkout failures:\n" + string.Join("\n", failures));

        var refreshed = await clients[0].GetFromJsonAsync<JsonElement>($"/api/products/{productId}");
        Assert.Equal(16, Property(refreshed, "stock").GetInt32());
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
        var dealer = Property(users, "items").EnumerateArray()
            .Single(x => Property(x, "email").GetString() == "bayi@u1.local");

        var id = Property(dealer, "id").GetInt32();
        var version = Property(dealer, "version").GetInt32();
        var newPassword = "CINewPassword!2026";

        var firstUpdate = await SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = Property(dealer, "firstName").GetString(),
            lastName = Property(dealer, "lastName").GetString(),
            email = Property(dealer, "email").GetString(),
            phone = Property(dealer, "phone").GetString(),
            company = Property(dealer, "company").GetString(),
            isActive = true,
            newPassword,
            version
        }, adminToken);
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await SendJson(admin, HttpMethod.Put, $"/api/admin/users/{id}", new
        {
            firstName = Property(dealer, "firstName").GetString(),
            lastName = Property(dealer, "lastName").GetString(),
            email = Property(dealer, "email").GetString(),
            phone = Property(dealer, "phone").GetString(),
            company = "Stale Admin Form",
            isActive = true,
            newPassword = (string?)null,
            version
        }, adminToken);

        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        var conflict = await staleUpdate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("USER_CHANGED", Property(conflict, "code").GetString());

        using var dealerClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var dealerAnonymousToken = await GetCsrf(dealerClient);
        var dealerLogin = await SendJson(dealerClient, HttpMethod.Post, "/api/auth/login",
            new { email = "bayi@u1.local", password = newPassword }, dealerAnonymousToken);
        Assert.Equal(HttpStatusCode.OK, dealerLogin.StatusCode);
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        throw new KeyNotFoundException($"JSON property '{name}' was not found.");
    }

    private static async Task<string> GetCsrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/csrf");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Property(body, "token").GetString()!;
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
