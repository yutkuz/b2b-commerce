using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Endpoints;
using U1.Business.Services;
using Xunit;

namespace U1.Business.Tests;

[Collection(ApiTestCollection.Name)]
public sealed class CategoryConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Concurrent_creates_with_the_same_name_leave_one_category_and_audit()
    {
        var token = TestContext.Current.CancellationToken;
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var firstCsrf = await ApiTest.LoginAdmin(firstClient);
        var secondCsrf = await ApiTest.LoginAdmin(secondClient);
        var name = "R23 create " + Guid.NewGuid().ToString("N")[..8];

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(token);
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.Serializable, token);
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = (SqlTransaction)blockerTx;
            command.CommandText = "SELECT * FROM Categories WITH(UPDLOCK,HOLDLOCK) WHERE Name=@name";
            command.Parameters.AddWithValue("@name", name);
            using var reader = await command.ExecuteReaderAsync(token);
            Assert.False(await reader.ReadAsync(token));
        }

        var firstTask = ApiTest.SendJson(firstClient, HttpMethod.Post,
            "/api/admin/categories", new { name }, firstCsrf);
        var secondTask = ApiTest.SendJson(secondClient, HttpMethod.Post,
            "/api/admin/categories", new { name }, secondCsrf);
        Exception? waitFailure = null;
        try
        {
            await WaitForBlockedRequests(2, token);
        }
        catch (Exception ex)
        {
            waitFailure = ex;
        }
        finally
        {
            await blockerTx.CommitAsync(CancellationToken.None);
        }

        using var first = await firstTask;
        using var second = await secondTask;
        if (waitFailure is not null)
            throw waitFailure;
        Assert.Contains(HttpStatusCode.OK, new[] { first.StatusCode, second.StatusCode });
        Assert.Contains(HttpStatusCode.Conflict, new[] { first.StatusCode, second.StatusCode });
        await using var check = await NewDb(token);
        var categoryId = await check.Categories.Where(x => x.Name == name).Select(x => x.Id).SingleAsync(token);
        Assert.Equal(1, await check.AdminEvents.CountAsync(x =>
            x.EventType == "CategoryCreated" && x.EntityId == categoryId, token));
    }

    [Fact]
    public async Task Concurrent_renames_of_the_same_category_keep_one_version_and_audit()
    {
        var token = TestContext.Current.CancellationToken;
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var firstCsrf = await ApiTest.LoginAdmin(firstClient);
        var secondCsrf = await ApiTest.LoginAdmin(secondClient);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateCategory(firstClient, firstCsrf, "R23 rename " + suffix);
        var categories = await firstClient.GetFromJsonAsync<JsonElement>("/api/admin/categories", token);
        var version = ApiTest.Property(categories.EnumerateArray().Single(x =>
            ApiTest.Property(x, "id").GetInt32() == id), "rowVersion").GetString()!;

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(token);
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.Serializable, token);
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = (SqlTransaction)blockerTx;
            command.CommandText = "SELECT * FROM Categories WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id";
            command.Parameters.AddWithValue("@id", id);
            using var reader = await command.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
        }

        var firstTask = ApiTest.SendJson(firstClient, HttpMethod.Put, $"/api/admin/categories/{id}",
            new { name = "R23 first " + suffix, rowVersion = version }, firstCsrf);
        var secondTask = ApiTest.SendJson(secondClient, HttpMethod.Put, $"/api/admin/categories/{id}",
            new { name = "R23 second " + suffix, rowVersion = version }, secondCsrf);
        Exception? waitFailure = null;
        try
        {
            await WaitForBlockedRequests(2, token);
        }
        catch (Exception ex)
        {
            waitFailure = ex;
        }
        finally
        {
            await blockerTx.CommitAsync(CancellationToken.None);
        }

        using var first = await firstTask;
        using var second = await secondTask;
        if (waitFailure is not null)
            throw waitFailure;
        Assert.Contains(HttpStatusCode.OK, new[] { first.StatusCode, second.StatusCode });
        Assert.Contains(HttpStatusCode.Conflict, new[] { first.StatusCode, second.StatusCode });
        await using var check = await NewDb(token);
        Assert.Contains((await check.Categories.SingleAsync(x => x.Id == id, token)).Name,
            new[] { "R23 first " + suffix, "R23 second " + suffix });
        Assert.Equal(1, await check.AdminEvents.CountAsync(x =>
            x.EventType == "CategoryUpdated" && x.EntityId == id, token));
    }

    [Fact]
    public async Task Reverse_merges_wait_on_the_same_first_row_and_commit_only_one_audit()
    {
        var token = TestContext.Current.CancellationToken;
        using var firstClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var secondClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var firstCsrf = await ApiTest.LoginAdmin(firstClient);
        var secondCsrf = await ApiTest.LoginAdmin(secondClient);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstId = await CreateCategory(firstClient, firstCsrf, "R23 A " + suffix);
        var secondId = await CreateCategory(firstClient, firstCsrf, "R23 B " + suffix);
        await using (var db = await NewDb(token))
        {
            db.Products.Add(new Product
            {
                Code = "R23-" + suffix.ToUpperInvariant(),
                Name = "R23 yarışı",
                Description = "CI",
                Brand = "CI",
                ManufacturerCode = "CI",
                ImageUrl = "/images/product.svg",
                Stock = 3,
                CriticalStock = 1,
                Price = 100m,
                CategoryId = firstId
            });
            await db.SaveChangesAsync(token);
        }

        var categories = await firstClient.GetFromJsonAsync<JsonElement>("/api/admin/categories", token);
        string Version(int id) => ApiTest.Property(categories.EnumerateArray().Single(x =>
            ApiTest.Property(x, "id").GetInt32() == id), "rowVersion").GetString()!;
        var firstVersion = Version(firstId);
        var secondVersion = Version(secondId);

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(token);
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.Serializable, token);
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = (SqlTransaction)blockerTx;
            command.CommandText = "SELECT * FROM Categories WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id";
            command.Parameters.AddWithValue("@id", Math.Min(firstId, secondId));
            using var reader = await command.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
        }

        var firstMerge = ApiTest.SendJson(firstClient, HttpMethod.Post,
            $"/api/admin/categories/{firstId}/merge",
            new { targetCategoryId = secondId, sourceRowVersion = firstVersion, targetRowVersion = secondVersion },
            firstCsrf);
        var secondMerge = ApiTest.SendJson(secondClient, HttpMethod.Post,
            $"/api/admin/categories/{secondId}/merge",
            new { targetCategoryId = firstId, sourceRowVersion = secondVersion, targetRowVersion = firstVersion },
            secondCsrf);

        Exception? waitFailure = null;
        try
        {
            await WaitForBlockedRequests(2, token);
        }
        catch (Exception ex)
        {
            waitFailure = ex;
        }
        finally
        {
            await blockerTx.CommitAsync(CancellationToken.None);
        }

        using var firstResponse = await firstMerge;
        using var secondResponse = await secondMerge;
        if (waitFailure is not null)
            throw waitFailure;
        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        Assert.Contains(HttpStatusCode.OK, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
        var conflict = firstResponse.StatusCode == HttpStatusCode.Conflict ? firstResponse : secondResponse;
        Assert.Equal("CATEGORY_CHANGED", ApiTest.Property(
            await conflict.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        await using var check = await NewDb(token);
        var remaining = await check.Categories
            .Where(x => x.Id == firstId || x.Id == secondId)
            .Select(x => x.Id).SingleAsync(token);
        Assert.Equal(remaining, await check.Products.Where(x => x.Code == "R23-" + suffix.ToUpperInvariant())
            .Select(x => x.CategoryId).SingleAsync(token));
        Assert.Equal(1, await check.AdminEvents.CountAsync(x =>
            x.EventType == "CategoryMerged" && x.EntityId == remaining, token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merge_and_product_write_preserve_the_product_and_audit_when_the_row_is_contended(bool edit)
    {
        var token = TestContext.Current.CancellationToken;
        using var mergeClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var archiveClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var mergeCsrf = await ApiTest.LoginAdmin(mergeClient);
        var archiveCsrf = await ApiTest.LoginAdmin(archiveClient);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceId = await CreateCategory(mergeClient, mergeCsrf, "R23 source " + suffix);
        var targetId = await CreateCategory(mergeClient, mergeCsrf, "R23 target " + suffix);
        int productId;
        await using (var db = await NewDb(token))
        {
            var product = new Product
            {
                Code = "R23-P-" + suffix.ToUpperInvariant(),
                Name = "R23 ürün",
                Description = "CI",
                Brand = "CI",
                ManufacturerCode = "CI",
                ImageUrl = "/images/product.svg",
                Stock = 3,
                CriticalStock = 1,
                Price = 100m,
                CategoryId = sourceId
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(token);
            productId = product.Id;
        }

        var categories = await mergeClient.GetFromJsonAsync<JsonElement>("/api/admin/categories", token);
        string Version(int id) => ApiTest.Property(categories.EnumerateArray().Single(x =>
            ApiTest.Property(x, "id").GetInt32() == id), "rowVersion").GetString()!;
        var productInfo = await archiveClient.GetFromJsonAsync<JsonElement>(
            $"/api/admin/products/{productId}", token);
        var productVersion = ApiTest.Property(productInfo, "rowVersion").GetString()!;

        await using var blocker = new SqlConnection(ApiFactory.ConnectionString);
        await blocker.OpenAsync(token);
        await using var blockerTx = await blocker.BeginTransactionAsync(IsolationLevel.Serializable, token);
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = (SqlTransaction)blockerTx;
            command.CommandText = "SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id=@id";
            command.Parameters.AddWithValue("@id", productId);
            using var reader = await command.ExecuteReaderAsync(token);
            Assert.True(await reader.ReadAsync(token));
        }

        var mergeTask = ApiTest.SendJson(mergeClient, HttpMethod.Post,
            $"/api/admin/categories/{sourceId}/merge",
            new { targetCategoryId = targetId, sourceRowVersion = Version(sourceId), targetRowVersion = Version(targetId) },
            mergeCsrf);
        var productWriteTask = edit
            ? ApiTest.SendJson(archiveClient, HttpMethod.Put, $"/api/admin/products/{productId}",
                new
                {
                    code = "R23-P-" + suffix.ToUpperInvariant(),
                    name = "R23 düzenlendi",
                    description = "CI",
                    brand = "CI",
                    manufacturerCode = "CI",
                    imageUrl = "/images/product.svg",
                    stock = 3,
                    criticalStock = 1,
                    price = 100m,
                    categoryId = sourceId,
                    version = productVersion
                }, archiveCsrf)
            : ApiTest.SendJson(archiveClient, HttpMethod.Post, $"/api/admin/products/{productId}/archive",
                new { rowVersion = productVersion, reason = "R23 yarış" }, archiveCsrf);

        Exception? waitFailure = null;
        try
        {
            await WaitForBlockedRequests(2, token);
        }
        catch (Exception ex)
        {
            waitFailure = ex;
        }
        finally
        {
            await blockerTx.CommitAsync(CancellationToken.None);
        }

        using var merge = await mergeTask;
        using var productWrite = await productWriteTask;
        if (waitFailure is not null)
            throw waitFailure;
        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        Assert.True(productWrite.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        await using var check = await NewDb(token);
        var finalProduct = await check.Products.SingleAsync(x => x.Id == productId, token);
        Assert.Equal(targetId, finalProduct.CategoryId);
        Assert.Equal(3, finalProduct.Stock);
        Assert.Equal(1, await check.AdminEvents.CountAsync(x =>
            x.EventType == "CategoryMerged" && x.EntityId == targetId, token));
        Assert.Equal(productWrite.StatusCode == HttpStatusCode.OK ? 1 : 0,
            await check.AdminEvents.CountAsync(x =>
                x.EventType == (edit ? "ProductUpdated" : "ProductArchived") && x.EntityId == productId, token));
        Assert.Equal(!edit && productWrite.StatusCode == HttpStatusCode.OK, finalProduct.IsArchived);
        Assert.Equal(edit && productWrite.StatusCode == HttpStatusCode.OK ? "R23 düzenlendi" : "R23 ürün",
            finalProduct.Name);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public async Task Sql_timeouts_retry_with_fresh_transactions_and_leave_no_partial_category(
        int failedAttempts, bool succeeds)
    {
        var token = TestContext.Current.CancellationToken;
        var name = "R23 timeout " + Guid.NewGuid().ToString("N")[..8];
        var attempts = 0;
        var contexts = new HashSet<BusinessDbContext>(ReferenceEqualityComparer.Instance);

        async Task<IResult> Operation(BusinessDbContext db)
        {
            Assert.True(contexts.Add(db));
            attempts++;
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var category = new Category { Name = name };
            db.Categories.Add(category);
            await db.SaveChangesAsync(token);
            if (attempts <= failedAttempts)
            {
                db.Database.SetCommandTimeout(1);
                await db.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:02'", token);
            }

            db.AdminEvents.Add(AuditTrail.Event(1, "CategoryCreated", "Category", category.Id, "R23 test " + name));
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return Results.Ok();
        }

        if (succeeds)
            await AdminEndpoints.RetryCategoryWrite(
                factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>(), Operation);
        else
        {
            var error = await Assert.ThrowsAsync<BusinessException>(() =>
                AdminEndpoints.RetryCategoryWrite(
                    factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>(), Operation));
            Assert.Equal(503, error.Status);
            Assert.Equal("CATEGORY_RETRY", error.Code);
        }

        Assert.Equal(succeeds ? failedAttempts + 1 : 3, attempts);
        await using var check = await NewDb(token);
        Assert.Equal(succeeds ? 1 : 0, await check.Categories.CountAsync(x => x.Name == name, token));
        Assert.Equal(succeeds ? 1 : 0, await check.AdminEvents.CountAsync(x =>
            x.EventType == "CategoryCreated" && x.Summary == "R23 test " + name, token));
    }

    private async Task<int> CreateCategory(HttpClient client, string csrf, string name)
    {
        using var response = await ApiTest.SendJson(client, HttpMethod.Post,
            "/api/admin/categories", new { name }, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ApiTest.Property(await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken), "id").GetInt32();
    }

    private Task<BusinessDbContext> NewDb(CancellationToken token) =>
        factory.Services.GetRequiredService<IDbContextFactory<BusinessDbContext>>()
            .CreateDbContextAsync(token);

    private static async Task WaitForBlockedRequests(int expected, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        await using var probe = new SqlConnection(ApiFactory.ConnectionString);
        await probe.OpenAsync(token);
        using var count = probe.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND wait_type LIKE 'LCK_M_%'";
        while (DateTime.UtcNow < deadline)
        {
            if (Convert.ToInt32(await count.ExecuteScalarAsync(token)) >= expected)
                return;
            await Task.Delay(25, token);
        }

        throw new TimeoutException($"{expected} kategori isteği SQL kilidinde beklemedi.");
    }
}
