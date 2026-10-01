using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class ImageCleanupRaceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Cleanup_cannot_delete_an_image_attached_by_an_in_flight_product_write()
    {
        var token = TestContext.Current.CancellationToken;
        var webRoot = NewWebRoot();
        await using var barrier = new SqlConnection(ApiFactory.ConnectionString);
        SqlTransaction? barrierTransaction = null;
        try
        {
            using var imageFactory = NewFactory(webRoot);
            using var admin = NewAdmin(imageFactory);
            var csrf = await ApiTest.LoginAdmin(admin);
            var (url, path) = await CreateOldImage(webRoot, token);
            var productCode = "CI-IMG-FIRST-" + Guid.NewGuid().ToString("N")[..8];

            await barrier.OpenAsync(token);
            barrierTransaction = (SqlTransaction)await barrier.BeginTransactionAsync(token);
            await using (var command = new SqlCommand(
                "SELECT TOP (1) Id FROM dbo.Products WITH (TABLOCKX,HOLDLOCK)",
                barrier,
                barrierTransaction))
            {
                await command.ExecuteScalarAsync(token);
            }
            int barrierSessionId;
            await using (var command = new SqlCommand("SELECT @@SPID", barrier, barrierTransaction))
                barrierSessionId = Convert.ToInt32(await command.ExecuteScalarAsync(token));

            var createTask = ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
                ProductPayload(productCode, url), csrf);
            await WaitUntilAsync(async () =>
            {
                await using var check = new SqlConnection(ApiFactory.ConnectionString);
                await check.OpenAsync(token);
                await using var command = new SqlCommand(
                    "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = @sessionId",
                    check);
                command.Parameters.AddWithValue("@sessionId", barrierSessionId);
                return Convert.ToInt32(await command.ExecuteScalarAsync(token)) > 0;
            }, token);

            var cleanupTask = ApiTest.SendJson(admin, HttpMethod.Post,
                "/api/admin/images/cleanup", new { urls = new[] { url } }, csrf);
            await barrierTransaction.CommitAsync(token);
            await barrierTransaction.DisposeAsync();
            barrierTransaction = null;

            using var created = await createTask;
            using var cleanup = await cleanupTask;
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, cleanup.StatusCode);
            var error = await cleanup.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal("IMAGE_IN_USE", ApiTest.Property(error, "code").GetString());
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (barrierTransaction is not null)
                await barrierTransaction.RollbackAsync(CancellationToken.None);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Product_cannot_attach_an_image_after_cleanup_has_staged_it()
    {
        var token = TestContext.Current.CancellationToken;
        var webRoot = NewWebRoot();
        await using var barrier = new SqlConnection(ApiFactory.ConnectionString);
        SqlTransaction? barrierTransaction = null;
        try
        {
            using var imageFactory = NewFactory(webRoot);
            using var admin = NewAdmin(imageFactory);
            var csrf = await ApiTest.LoginAdmin(admin);
            var (url, path) = await CreateOldImage(webRoot, token);

            await barrier.OpenAsync(token);
            barrierTransaction = (SqlTransaction)await barrier.BeginTransactionAsync(token);
            await using (var command = new SqlCommand(
                "SELECT TOP (1) Id FROM dbo.AdminEvents WITH (TABLOCKX,HOLDLOCK)",
                barrier,
                barrierTransaction))
            {
                await command.ExecuteScalarAsync(token);
            }

            var cleanupTask = ApiTest.SendJson(admin, HttpMethod.Post,
                "/api/admin/images/cleanup", new { urls = new[] { url } }, csrf);
            await WaitUntil(() => !File.Exists(path)
                && Directory.Exists(Path.Combine(webRoot, "uploads", ".pending-cleanup"))
                && Directory.EnumerateFiles(
                    Path.Combine(webRoot, "uploads", ".pending-cleanup"),
                    "*", SearchOption.AllDirectories).Any(), token);
            var stagedPath = Directory.EnumerateFiles(
                Path.Combine(webRoot, "uploads", ".pending-cleanup"),
                "*", SearchOption.AllDirectories).Single();
            var stagedUrl = "/" + Path.GetRelativePath(webRoot, stagedPath).Replace('\\', '/');
            using var hidden = await admin.GetAsync(stagedUrl, token);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

            var productCode = "CI-IMG-RACE-" + Guid.NewGuid().ToString("N")[..8];
            var createTask = ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products",
                ProductPayload(productCode, url), csrf);
            await barrierTransaction.CommitAsync(token);
            await barrierTransaction.DisposeAsync();
            barrierTransaction = null;

            using var cleanup = await cleanupTask;
            using var create = await createTask;
            Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
            var error = await create.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal("IMAGE_CHANGED", ApiTest.Property(error, "code").GetString());
            Assert.False(File.Exists(path));

            using var scope = imageFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.False(await db.Products.AnyAsync(x => x.Code == productCode, token));
        }
        finally
        {
            if (barrierTransaction is not null)
                await barrierTransaction.RollbackAsync(CancellationToken.None);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_audit_restores_the_staged_image()
    {
        var token = TestContext.Current.CancellationToken;
        var webRoot = NewWebRoot();
        try
        {
            using var imageFactory = NewFactory(webRoot);
            using var admin = NewAdmin(imageFactory);
            var csrf = await ApiTest.LoginAdmin(admin);
            var (url, path) = await CreateOldImage(webRoot, token);

            await ExecuteSql("""
                ALTER TABLE dbo.AdminEvents WITH NOCHECK ADD CONSTRAINT CK_CI_ImageCleanupFailure
                CHECK (EventType <> 'ImageCleanup')
                """, token);

            using var response = await ApiTest.SendJson(admin, HttpMethod.Post,
                "/api/admin/images/cleanup", new { urls = new[] { url } }, csrf);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.True(File.Exists(path));
            var stagingRoot = Path.Combine(webRoot, "uploads", ".pending-cleanup");
            Assert.True(!Directory.Exists(stagingRoot)
                || !Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories).Any());

            using var scope = imageFactory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
            Assert.False(await db.AdminEvents.AnyAsync(x => x.EventType == "ImageCleanup", token));
        }
        finally
        {
            await ExecuteSql(
                "ALTER TABLE dbo.AdminEvents DROP CONSTRAINT IF EXISTS CK_CI_ImageCleanupFailure",
                CancellationToken.None);
            Directory.Delete(webRoot, recursive: true);
        }
    }

    private static object ProductPayload(string code, string imageUrl) => new
    {
        code,
        name = "CI image race product",
        description = "CI",
        brand = "CI",
        manufacturerCode = "CI",
        specialCode1 = "CI",
        specialCode2 = "",
        imageUrl,
        stock = 1,
        criticalStock = 0,
        price = 1m,
        categoryId = 1,
        stockReason = "CI initial stock"
    };

    private WebApplicationFactory<Program> NewFactory(string webRoot) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseWebRoot(webRoot);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ImageStorage:MinimumFreeSpaceBytes"] = "0",
                    ["ImageStorage:OrphanGraceMinutes"] = "0"
                }));
        });

    private static HttpClient NewAdmin(WebApplicationFactory<Program> imageFactory) =>
        imageFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private static string NewWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "u1-image-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<(string Url, string Path)> CreateOldImage(
        string webRoot,
        CancellationToken token)
    {
        var folder = Path.Combine(webRoot, "uploads");
        Directory.CreateDirectory(folder);
        var name = Guid.NewGuid().ToString("N") + ".png";
        var path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, [1, 2, 3], token);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
        return ("/uploads/" + name, path);
    }

    private static async Task ExecuteSql(string sql, CancellationToken token)
    {
        await using var connection = new SqlConnection(ApiFactory.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(7))
                throw new TimeoutException("Görsel temizleme aşaması başlamadı.");
            await Task.Delay(25, token);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(7))
                throw new TimeoutException("Ürün yazması SQL kilidine ulaşmadı.");
            await Task.Delay(25, token);
        }
    }
}
