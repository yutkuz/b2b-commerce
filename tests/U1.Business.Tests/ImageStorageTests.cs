using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using U1.Business.Data;
using U1.Business.Domain;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class ImageStorageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Quota_preview_grace_reference_and_cleanup_rules_are_enforced()
    {
        var token = TestContext.Current.CancellationToken;
        var webRoot = Path.Combine(
            Path.GetTempPath(),
            "u1-image-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        var bytes = EncodePng();

        try
        {
            using var imageFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(webRoot);
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ImageStorage:TotalQuotaBytes"] = (bytes.Length + 8L).ToString(),
                        ["ImageStorage:MinimumFreeSpaceBytes"] = "0",
                        ["ImageStorage:OrphanGraceMinutes"] = "60",
                        ["ImageStorage:CleanupBatchLimit"] = "10",
                        ["RateLimits:ImageUpload:PermitLimit"] = "20"
                    }));
            });

            using var admin = imageFactory.CreateClient(
                new WebApplicationFactoryClientOptions { HandleCookies = true });
            var csrf = await ApiTest.LoginAdmin(admin);

            var uploads = await Task.WhenAll(
                Upload(admin, csrf, bytes, token),
                Upload(admin, csrf, bytes, token));
            Assert.Single(uploads, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(uploads, x => x.StatusCode == HttpStatusCode.Conflict);
            using var successful = uploads.Single(x => x.StatusCode == HttpStatusCode.OK);
            using var quotaFailure = uploads.Single(x => x.StatusCode == HttpStatusCode.Conflict);
            var quotaError = await quotaFailure.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal("IMAGE_STORAGE_QUOTA", ApiTest.Property(quotaError, "code").GetString());

            var body = await successful.Content.ReadFromJsonAsync<JsonElement>(token);
            var url = ApiTest.Property(body, "url").GetString()!;
            var uploadFolder = Path.Combine(webRoot, "uploads");
            var savedPath = Path.Combine(uploadFolder, Path.GetFileName(url));
            Assert.True(File.Exists(savedPath));
            Assert.Single(Directory.EnumerateFiles(uploadFolder));
            Assert.Empty(Directory.EnumerateFiles(uploadFolder, "*.uploading"));

            var preview = await admin.GetFromJsonAsync<JsonElement>(
                "/api/admin/images/cleanup-preview",
                token);
            var candidate = ApiTest.Property(preview, "items").EnumerateArray()
                .Single(x => ApiTest.Property(x, "url").GetString() == url);
            Assert.False(ApiTest.Property(candidate, "canDelete").GetBoolean());

            File.SetLastWriteTimeUtc(savedPath, DateTime.UtcNow.AddHours(-2));
            preview = await admin.GetFromJsonAsync<JsonElement>(
                "/api/admin/images/cleanup-preview",
                token);
            candidate = ApiTest.Property(preview, "items").EnumerateArray()
                .Single(x => ApiTest.Property(x, "url").GetString() == url);
            Assert.True(ApiTest.Property(candidate, "canDelete").GetBoolean());

            int productId;
            using (var scope = imageFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                var product = new Product
                {
                    Code = "CI-IMG-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                    Name = "CI image reference",
                    Description = "CI",
                    Brand = "CI",
                    ManufacturerCode = "CI",
                    ImageUrl = url,
                    Stock = 1,
                    CriticalStock = 0,
                    Price = 1m,
                    CategoryId = 1
                };
                db.Products.Add(product);
                await db.SaveChangesAsync(token);
                productId = product.Id;
            }

            preview = await admin.GetFromJsonAsync<JsonElement>(
                "/api/admin/images/cleanup-preview",
                token);
            Assert.DoesNotContain(
                ApiTest.Property(preview, "items").EnumerateArray(),
                x => ApiTest.Property(x, "url").GetString() == url);

            using var inUse = await ApiTest.SendJson(
                admin,
                HttpMethod.Post,
                "/api/admin/images/cleanup",
                new { urls = new[] { url } },
                csrf);
            Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
            var inUseError = await inUse.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal("IMAGE_IN_USE", ApiTest.Property(inUseError, "code").GetString());
            Assert.True(File.Exists(savedPath));

            using (var scope = imageFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                await db.Products.Where(x => x.Id == productId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.ImageUrl, "/images/product.svg"),
                        token);
            }

            var sentinel = Path.Combine(webRoot, "sentinel.txt");
            await File.WriteAllTextAsync(sentinel, "keep", token);
            using var traversal = await ApiTest.SendJson(
                admin,
                HttpMethod.Post,
                "/api/admin/images/cleanup",
                new { urls = new[] { "/uploads/../sentinel.txt" } },
                csrf);
            Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
            Assert.True(File.Exists(sentinel));

            using var cleanup = await ApiTest.SendJson(
                admin,
                HttpMethod.Post,
                "/api/admin/images/cleanup",
                new { urls = new[] { url } },
                csrf);
            Assert.Equal(HttpStatusCode.OK, cleanup.StatusCode);
            Assert.False(File.Exists(savedPath));

            using (var scope = imageFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
                Assert.True(await db.AdminEvents.AnyAsync(
                    x => x.EventType == "ImageCleanup",
                    token));
            }
        }
        finally
        {
            if (Directory.Exists(webRoot))
                Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Low_free_space_guard_rejects_upload_without_partial_file()
    {
        var token = TestContext.Current.CancellationToken;
        var webRoot = Path.Combine(
            Path.GetTempPath(),
            "u1-image-space-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        var bytes = EncodePng();

        try
        {
            using var imageFactory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseWebRoot(webRoot);
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ImageStorage:TotalQuotaBytes"] = (10L * 1024 * 1024).ToString(),
                        ["ImageStorage:MinimumFreeSpaceBytes"] = long.MaxValue.ToString(),
                        ["ImageStorage:OrphanGraceMinutes"] = "0"
                    }));
            });

            using var admin = imageFactory.CreateClient(
                new WebApplicationFactoryClientOptions { HandleCookies = true });
            var csrf = await ApiTest.LoginAdmin(admin);
            using var response = await Upload(admin, csrf, bytes, token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal("IMAGE_STORAGE_LOW_SPACE", ApiTest.Property(error, "code").GetString());

            var folder = Path.Combine(webRoot, "uploads");
            Assert.True(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder).Any());
        }
        finally
        {
            if (Directory.Exists(webRoot))
                Directory.Delete(webRoot, recursive: true);
        }
    }

    private static async Task<HttpResponseMessage> Upload(
        HttpClient client,
        string csrf,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "quota.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/images")
        {
            Content = form
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request, cancellationToken);
    }

    private static byte[] EncodePng()
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.SlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException("Test görseli kodlanamadı.");
        return data.ToArray();
    }
}
