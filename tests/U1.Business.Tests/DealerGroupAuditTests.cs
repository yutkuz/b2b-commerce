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
public sealed class DealerGroupAuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Creation_and_audit_commit_together_and_duplicate_name_remains_conflict()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var name = "R25 " + Guid.NewGuid().ToString("N")[..12];
        using var created = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/dealer-groups",
            new { name, discountPercent = 12.5m }, csrf);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = ApiTest.Property(await created.Content.ReadFromJsonAsync<JsonElement>(token), "id").GetInt32();
        using var duplicate = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/dealer-groups",
            new { name, discountPercent = 20m }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("DEALER_GROUP_NAME_EXISTS", ApiTest.Property(
            await duplicate.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());
        await using var db = await NewDb(token);
        Assert.Equal(1, await db.DealerGroups.CountAsync(x => x.Name == name, token));
        Assert.Equal(12.5m, await db.DealerGroups.Where(x => x.Id == id)
            .Select(x => x.DiscountPercent).SingleAsync(token));
        Assert.Equal(1, await db.AdminEvents.CountAsync(x =>
            x.EventType == "DealerGroupCreated" && x.EntityType == "DealerGroup" && x.EntityId == id, token));
    }

    [Fact]
    public async Task Audit_write_failure_rolls_back_group_and_retry_creates_one_group_and_event()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var name = "R25 rollback " + Guid.NewGuid().ToString("N")[..12];
        await using var db = await NewDb(token);
        var eventsBefore = await db.AdminEvents.CountAsync(x => x.EventType == "DealerGroupCreated", token);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE dbo.AdminEvents WITH NOCHECK ADD CONSTRAINT CK_CI_DealerGroupAuditFailure
                CHECK (EventType <> 'DealerGroupCreated')
                """, token);
            using var failed = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/dealer-groups",
                new { name, discountPercent = 5m }, csrf);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.False(await db.DealerGroups.AnyAsync(x => x.Name == name, token));
            Assert.Equal(eventsBefore, await db.AdminEvents.CountAsync(x => x.EventType == "DealerGroupCreated", token));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE dbo.AdminEvents DROP CONSTRAINT IF EXISTS CK_CI_DealerGroupAuditFailure",
                CancellationToken.None);
        }
        using var retry = await ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/dealer-groups",
            new { name, discountPercent = 5m }, csrf);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var id = ApiTest.Property(await retry.Content.ReadFromJsonAsync<JsonElement>(token), "id").GetInt32();
        Assert.Equal(1, await db.DealerGroups.CountAsync(x => x.Name == name, token));
        Assert.Equal(1, await db.AdminEvents.CountAsync(x => x.EventType == "DealerGroupCreated" && x.EntityId == id, token));
    }

    private Task<BusinessDbContext> NewDb(CancellationToken token) =>
        factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>().CreateDbContextAsync(token);
}
