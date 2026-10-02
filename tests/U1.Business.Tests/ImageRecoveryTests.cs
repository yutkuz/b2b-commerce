using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class ImageRecoveryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("restore")]
    [InlineData("finalize")]
    public async Task Completed_files_return_success_with_operation_and_warning_when_result_audit_fails(string action)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "u1-image-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var recoveryFactory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
        using var admin = recoveryFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var operation = Guid.NewGuid().ToString("N");
        var name = Guid.NewGuid().ToString("N") + ".png";
        var folder = Path.Combine(root, "uploads", ".pending-cleanup", operation);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, name), "recovery-content", token);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>().CreateDbContextAsync(token);
        try
        {
            db.AdminEvents.Add(AuditTrail.Event(1, "ImageCleanup", "UploadStorage", null,
                $"1 kullanılmayan ürün görseli {operation} işlemiyle temizlik için ayrıldı."));
            await db.SaveChangesAsync(token);
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE dbo.AdminEvents WITH NOCHECK ADD CONSTRAINT CK_CI_ImageRecoveryAuditFailure
                CHECK (EventType <> 'ImageCleanupRecovered')
                """, token);
            using var response = await ApiTest.SendJson(admin, HttpMethod.Post,
                $"/api/admin/images/cleanup-pending/{operation}/{action}", new { }, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal(operation, ApiTest.Property(result, "operationId").GetString());
            Assert.True(ApiTest.Property(result, "completed").GetBoolean());
            Assert.Equal(0, ApiTest.Property(result, "pending").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(ApiTest.Property(result, "auditWarning").GetString()));
            Assert.False(Directory.Exists(folder));
            var original = Path.Combine(root, "uploads", name);
            Assert.Equal(action == "restore", File.Exists(original));
            if (action == "restore")
                Assert.Equal("recovery-content", await File.ReadAllTextAsync(original, token));
            Assert.Equal(1, await db.AdminEvents.CountAsync(x => x.EventType == "ImageCleanupRecoveryRequested"
                && x.Summary.Contains(operation), token));
            Assert.False(await db.AdminEvents.AnyAsync(x => x.EventType == "ImageCleanupRecovered"
                && x.Summary.Contains(operation), token));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE dbo.AdminEvents DROP CONSTRAINT IF EXISTS CK_CI_ImageRecoveryAuditFailure", CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Partially_restored_batch_resumes_with_remaining_files_and_preserves_restored_file()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "u1-image-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var recoveryFactory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
            using var admin = recoveryFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var csrf = await ApiTest.LoginAdmin(admin);
            var operation = Guid.NewGuid().ToString("N");
            var folder = Path.Combine(root, "uploads", ".pending-cleanup", operation);
            Directory.CreateDirectory(folder);
            var firstName = Guid.NewGuid().ToString("N") + ".png";
            var remainingName = Guid.NewGuid().ToString("N") + ".png";
            await File.WriteAllTextAsync(Path.Combine(folder, firstName), "already-restored", token);
            await File.WriteAllTextAsync(Path.Combine(folder, remainingName), "remaining", token);
            var firstOriginal = Path.Combine(root, "uploads", firstName);
            // Reproduce the on-disk state after interruption between two File.Move calls.
            File.Move(Path.Combine(folder, firstName), firstOriginal);
            var preview = await admin.GetFromJsonAsync<JsonElement>("/api/admin/images/cleanup-pending", token);
            var item = ApiTest.Property(preview, "items").EnumerateArray().Single();
            Assert.True(ApiTest.Property(item, "canRestore").GetBoolean());
            Assert.Single(ApiTest.Property(item, "images").EnumerateArray());
            using var response = await ApiTest.SendJson(admin, HttpMethod.Post,
                $"/api/admin/images/cleanup-pending/{operation}/restore", new { }, csrf);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("already-restored", await File.ReadAllTextAsync(firstOriginal, token));
            Assert.Equal("remaining", await File.ReadAllTextAsync(Path.Combine(root, "uploads", remainingName), token));
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Interrupted_cleanup_requires_audit_and_references_and_never_overwrites(
        bool committed, bool referenced, bool collision)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "u1-image-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var recoveryFactory = factory.WithWebHostBuilder(builder => builder.UseWebRoot(root));
            using var admin = recoveryFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            var csrf = await ApiTest.LoginAdmin(admin);
            var operation = Guid.NewGuid().ToString("N");
            var name = Guid.NewGuid().ToString("N") + ".png";
            var url = "/uploads/" + name;
            var folder = Path.Combine(root, "uploads", ".pending-cleanup", operation);
            Directory.CreateDirectory(folder);
            var staged = Path.Combine(folder, name);
            var original = Path.Combine(root, "uploads", name);
            await File.WriteAllTextAsync(staged, "staged-content", token);
            // A partial staging sequence leaves other original files untouched.
            var untouched = Path.Combine(root, "uploads", "untouched.png");
            await File.WriteAllTextAsync(untouched, "untouched", token);
            if (collision)
                await File.WriteAllTextAsync(original, "existing-content", token);
            using (var scope = recoveryFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                if (committed)
                    db.AdminEvents.Add(AuditTrail.Event(1, "ImageCleanup", "UploadStorage", null,
                        $"1 kullanılmayan ürün görseli {operation} işlemiyle temizlik için ayrıldı."));
                if (referenced)
                    db.Products.Add(new Product
                    {
                        Code = "CI-RECOVERY-" + Guid.NewGuid().ToString("N")[..8],
                        Name = "Recovery reference", Description = "CI", Brand = "CI",
                        ManufacturerCode = "CI", ImageUrl = url, CategoryId = 1, Price = 1m
                    });
                await db.SaveChangesAsync(token);
            }
            var preview = await admin.GetFromJsonAsync<JsonElement>("/api/admin/images/cleanup-pending", token);
            var item = ApiTest.Property(preview, "items").EnumerateArray().Single();
            Assert.Equal(committed, ApiTest.Property(item, "auditCommitted").GetBoolean());
            Assert.Equal(committed && !referenced && !collision, ApiTest.Property(item, "canFinalize").GetBoolean());
            Assert.Equal(!collision, ApiTest.Property(item, "canRestore").GetBoolean());
            using var finalize = await ApiTest.SendJson(admin, HttpMethod.Post,
                $"/api/admin/images/cleanup-pending/{operation}/finalize", new { }, csrf);
            if (committed && !referenced && !collision)
            {
                Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
                Assert.False(File.Exists(staged));
                Assert.False(File.Exists(original));
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, finalize.StatusCode);
                Assert.True(File.Exists(staged));
                using var restore = await ApiTest.SendJson(admin, HttpMethod.Post,
                    $"/api/admin/images/cleanup-pending/{operation}/restore", new { }, csrf);
                if (collision)
                {
                    Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
                    Assert.Equal("existing-content", await File.ReadAllTextAsync(original, token));
                    Assert.Equal("staged-content", await File.ReadAllTextAsync(staged, token));
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
                    Assert.False(File.Exists(staged));
                    Assert.Equal("staged-content", await File.ReadAllTextAsync(original, token));
                }
            }
            Assert.Equal("untouched", await File.ReadAllTextAsync(untouched, token));
            using var repeat = await ApiTest.SendJson(admin, HttpMethod.Post,
                $"/api/admin/images/cleanup-pending/{operation}/restore", new { }, csrf);
            Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
            if (!collision)
            {
                using var scope = recoveryFactory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                Assert.True(await db.AdminEvents.AnyAsync(x => x.EventType == "ImageCleanupRecovered"
                    && x.Summary.Contains(operation), token));
            }
            using var traversal = await ApiTest.SendJson(admin, HttpMethod.Post,
                "/api/admin/images/cleanup-pending/not-a-guid/restore", new { }, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
