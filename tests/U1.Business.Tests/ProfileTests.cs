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
public sealed class ProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Dealer_can_update_only_own_profile_and_stale_form_is_rejected()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.GetCsrf(client);
        var email = $"ci-profile-{Guid.NewGuid():N}@example.test";
        using var register = await ApiTest.SendJson(client, HttpMethod.Post, "/api/auth/register", new
        {
            firstName = "Initial",
            lastName = "Dealer",
            email,
            phone = "05321234567",
            company = "Initial Company",
            password = "Dealer123!"
        }, csrf);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        csrf = await ApiTest.GetCsrf(client);

        var initial = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", TestContext.Current.CancellationToken);
        var initialVersion = ApiTest.Property(initial, "rowVersion").GetString()!;

        using var update = await ApiTest.SendJson(client, HttpMethod.Put, "/api/auth/profile", new
        {
            firstName = "Updated",
            lastName = "Dealer",
            phone = "05329876543",
            company = "Updated Company",
            rowVersion = initialVersion,
            email = "ignored@example.test",
            role = "Admin",
            isActive = false
        }, csrf);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("Updated", ApiTest.Property(updated, "firstName").GetString());
        Assert.Equal(email, ApiTest.Property(updated, "email").GetString());
        Assert.Equal("Dealer", ApiTest.Property(updated, "role").GetString());
        Assert.True(ApiTest.Property(updated, "isActive").GetBoolean());

        using var stale = await ApiTest.SendJson(client, HttpMethod.Put, "/api/auth/profile", new
        {
            firstName = "Stale",
            lastName = "Draft",
            phone = "05320000000",
            company = "Stale Company",
            rowVersion = initialVersion
        }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("PROFILE_CHANGED", ApiTest.Property(staleBody, "code").GetString());

        var current = await client.GetFromJsonAsync<JsonElement>("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal("Updated", ApiTest.Property(current, "firstName").GetString());
        Assert.Equal("Updated Company", ApiTest.Property(current, "company").GetString());
        Assert.Equal(email, ApiTest.Property(current, "email").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        var userId = ApiTest.Property(current, "id").GetInt32();
        Assert.Equal(1, await db.AdminEvents.CountAsync(
            x => x.ActorUserId == userId && x.EntityId == userId && x.EventType == "DealerProfileUpdated",
            TestContext.Current.CancellationToken));
    }
}
