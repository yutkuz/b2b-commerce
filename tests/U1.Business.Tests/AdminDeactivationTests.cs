using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class AdminDeactivationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_deactivation_preserves_the_last_active_admin_and_invalidates_the_target_session()
    {
        const string password = "CISecondAdmin!2026";
        var email = $"ci-admin-{Guid.NewGuid():N}@example.test";
        int secondId;
        int firstId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            firstId = await db.Users.Where(x => x.Email == "admin@u1.local")
                .Select(x => x.Id).SingleAsync(TestContext.Current.CancellationToken);
            var secondAdmin = new User
            {
                FirstName = "CI",
                LastName = "Second Admin",
                Email = email,
                Phone = "05321234567",
                Company = "CI",
                Role = "Admin",
                IsActive = true,
                AuthVersion = 1
            };
            secondAdmin.PasswordHash = new PasswordHasher<User>().HashPassword(secondAdmin, password);
            db.Users.Add(secondAdmin);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            secondId = secondAdmin.Id;
        }

        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var firstCsrf = await ApiTest.LoginAdmin(firstClient);
        var secondCsrf = await ApiTest.GetCsrf(secondClient);
        var login = await ApiTest.SendJson(secondClient, HttpMethod.Post, "/api/auth/login",
            new { email, password }, secondCsrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        secondCsrf = await ApiTest.GetCsrf(secondClient);

        var first = await GetUser(firstClient, "admin@u1.local");
        var second = await GetUser(firstClient, email);

        var self = await Deactivate(firstClient, firstCsrf, first);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal("ADMIN_SELF_DEACTIVATE", await ErrorCode(self));

        var deactivate = await Deactivate(firstClient, firstCsrf, second);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await secondClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.True(await db.AdminEvents.AnyAsync(x =>
                x.EventType == "UserStatusChanged" && x.EntityId == secondId,
                TestContext.Current.CancellationToken));
            await db.Users.Where(x => x.Id == secondId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsActive, true)
                .SetProperty(x => x.AuthVersion, x => x.AuthVersion + 1),
                TestContext.Current.CancellationToken);
        }

        using var freshSecond = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        secondCsrf = await ApiTest.GetCsrf(freshSecond);
        login = await ApiTest.SendJson(freshSecond, HttpMethod.Post, "/api/auth/login",
            new { email, password }, secondCsrf);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        secondCsrf = await ApiTest.GetCsrf(freshSecond);

        first = await GetUser(firstClient, "admin@u1.local");
        second = await GetUser(firstClient, email);
        var both = await Task.WhenAll(
            Deactivate(firstClient, firstCsrf, second),
            Deactivate(freshSecond, secondCsrf, first));
        Assert.Single(both, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(both, x => x.StatusCode == HttpStatusCode.Conflict
            || x.StatusCode == HttpStatusCode.Unauthorized);
        if (both.Any(x => x.StatusCode == HttpStatusCode.Conflict))
            Assert.Equal("LAST_ADMIN", await ErrorCode(both.Single(x => x.StatusCode == HttpStatusCode.Conflict)));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(x => x.Role == "Admin" && x.IsActive,
                TestContext.Current.CancellationToken));
            await db.Users.Where(x => x.Id == firstId).ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsActive, true)
                .SetProperty(x => x.AuthVersion, x => x.AuthVersion + 1),
                TestContext.Current.CancellationToken);
        }
    }

    private static async Task<JsonElement> GetUser(HttpClient client, string email)
    {
        var response = await client.GetFromJsonAsync<JsonElement>(
            "/api/admin/users?q=" + Uri.EscapeDataString(email), TestContext.Current.CancellationToken);
        return ApiTest.Property(response, "items").EnumerateArray()
            .Single(x => ApiTest.Property(x, "email").GetString() == email);
    }

    private static Task<HttpResponseMessage> Deactivate(HttpClient client, string csrf, JsonElement user) =>
        ApiTest.SendJson(client, HttpMethod.Put,
            $"/api/admin/users/{ApiTest.Property(user, "id").GetInt32()}", new
            {
                firstName = ApiTest.Property(user, "firstName").GetString(),
                lastName = ApiTest.Property(user, "lastName").GetString(),
                email = ApiTest.Property(user, "email").GetString(),
                phone = ApiTest.Property(user, "phone").GetString(),
                company = ApiTest.Property(user, "company").GetString(),
                isActive = false,
                version = ApiTest.Property(user, "version").GetInt32()
            }, csrf);

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return ApiTest.Property(body, "code").GetString();
    }
}
