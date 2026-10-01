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
public sealed class ProductCsvTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header="code,name,description,brand,manufacturerCode,specialCode1,specialCode2,imageUrl,stock,criticalStock,price,category,rowVersion,stockReason";

    [Fact]
    public async Task Csv_limits_and_quoted_fields_are_validated()
    {
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);

        var empty = await Preview(admin, csrf, Header);
        Assert.Contains(ApiTest.Property(empty, "errors").EnumerateArray(),
            x => ApiTest.Property(x, "code").GetString() == "EMPTY_BATCH");

        var large = await Preview(admin, csrf, Header + "\n" + new string('x', 1048576));
        Assert.Contains(ApiTest.Property(large, "errors").EnumerateArray(),
            x => ApiTest.Property(x, "code").GetString() == "FILE_TOO_LARGE");

        var tooMany = await Preview(admin, csrf,
            Header + "\n" + string.Join("\n", Enumerable.Range(1, 1001)
                .Select(index => Row($"CSV-LIMIT-{index}"))));
        Assert.Contains(ApiTest.Property(tooMany, "errors").EnumerateArray(),
            x => ApiTest.Property(x, "code").GetString() == "TOO_MANY_ROWS");

        var code = "CSV-QUOTE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var quoted = Csv($"{code},\"Ad, virgüllü\",Açıklama,CSV,CSV-M,,,/images/product.svg,0,1,10.00,Diagnostik cihazlar,,");
        var preview = await Preview(admin, csrf, quoted);
        Assert.True(ApiTest.Property(preview, "valid").GetBoolean());
        var change = ApiTest.Property(preview, "changes").EnumerateArray().Single();
        Assert.Equal("Ad, virgüllü", ApiTest.Property(
            ApiTest.Property(change, "after"), "name").GetString());
    }

    [Fact]
    public async Task Preview_shows_old_and_new_values_and_import_id_cannot_be_reused_for_other_content()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var code = "CSV-DIFF-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var initialCsv = Csv(Row(code, stock: "2", reason: "İlk stok"));
        var initialPreview = await Preview(admin, csrf, initialCsv);
        using var initialApply = await ApiTest.SendJson(admin, HttpMethod.Post,
            "/api/admin/products/import/apply",
            new
            {
                csv = initialCsv,
                importId = Guid.NewGuid(),
                previewToken = ApiTest.Property(initialPreview, "previewToken").GetString()
            }, csrf);
        Assert.Equal(HttpStatusCode.OK, initialApply.StatusCode);

        var exported = await admin.GetStringAsync("/api/admin/products/export", token);
        var version = exported.Split('\n').Single(x => x.StartsWith(code + ",", StringComparison.Ordinal))
            .Split(',')[12];
        var updateCsv = Csv(Row(code, stock: "3", price: "15.00",
            version: version, reason: "Stok güncellemesi"));
        var preview = await Preview(admin, csrf, updateCsv);
        var change = ApiTest.Property(preview, "changes").EnumerateArray().Single();
        Assert.Equal("update", ApiTest.Property(change, "action").GetString());
        Assert.Equal(2, ApiTest.Property(ApiTest.Property(change, "before"), "stock").GetInt32());
        Assert.Equal(3, ApiTest.Property(ApiTest.Property(change, "after"), "stock").GetInt32());
        Assert.Equal(10m, ApiTest.Property(ApiTest.Property(change, "before"), "price").GetDecimal());
        Assert.Equal(15m, ApiTest.Property(ApiTest.Property(change, "after"), "price").GetDecimal());

        var importId = Guid.NewGuid();
        var previewToken = ApiTest.Property(preview, "previewToken").GetString();
        using var applied = await ApiTest.SendJson(admin, HttpMethod.Post,
            "/api/admin/products/import/apply",
            new { csv = updateCsv, importId, previewToken }, csrf);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        using var repeated = await ApiTest.SendJson(admin, HttpMethod.Post,
            "/api/admin/products/import/apply",
            new { csv = updateCsv, importId, previewToken }, csrf);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.True(ApiTest.Property(
            await repeated.Content.ReadFromJsonAsync<JsonElement>(token),
            "alreadyApplied").GetBoolean());

        using var reused = await ApiTest.SendJson(admin, HttpMethod.Post,
            "/api/admin/products/import/apply",
            new { csv = updateCsv + "\n", importId, previewToken }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("IMPORT_ID_REUSED", ApiTest.Property(
            await reused.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());
    }

    [Fact]
    public async Task Reverse_order_concurrent_imports_have_one_consistent_winner()
    {
        var token = TestContext.Current.CancellationToken;
        using var admin = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var csrf = await ApiTest.LoginAdmin(admin);
        var firstCode = "CSV-A-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var secondCode = "CSV-B-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var initialCsv = Csv(Row(firstCode), Row(secondCode));
        var initialPreview = await Preview(admin, csrf, initialCsv);
        using var initial = await ApiTest.SendJson(admin, HttpMethod.Post,
            "/api/admin/products/import/apply",
            new
            {
                csv = initialCsv,
                importId = Guid.NewGuid(),
                previewToken = ApiTest.Property(initialPreview, "previewToken").GetString()
            }, csrf);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        var exported = await admin.GetStringAsync("/api/admin/products/export", token);
        string Version(string code) => exported.Split('\n')
            .Single(x => x.StartsWith(code + ",", StringComparison.Ordinal)).Split(',')[12];
        var firstCsv = Csv(
            Row(firstCode, price: "11.00", version: Version(firstCode)),
            Row(secondCode, price: "11.00", version: Version(secondCode)));
        var secondCsv = Csv(
            Row(secondCode, price: "12.00", version: Version(secondCode)),
            Row(firstCode, price: "12.00", version: Version(firstCode)));
        var firstPreview = await Preview(admin, csrf, firstCsv);
        var secondPreview = await Preview(admin, csrf, secondCsv);

        var results = await Task.WhenAll(
            ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products/import/apply",
                new
                {
                    csv = firstCsv,
                    importId = Guid.NewGuid(),
                    previewToken = ApiTest.Property(firstPreview, "previewToken").GetString()
                }, csrf),
            ApiTest.SendJson(admin, HttpMethod.Post, "/api/admin/products/import/apply",
                new
                {
                    csv = secondCsv,
                    importId = Guid.NewGuid(),
                    previewToken = ApiTest.Property(secondPreview, "previewToken").GetString()
                }, csrf));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var conflict = results.Single(x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("IMPORT_PREVIEW_STALE", ApiTest.Property(
            await conflict.Content.ReadFromJsonAsync<JsonElement>(token), "code").GetString());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        var prices = await db.Products.Where(x => x.Code == firstCode || x.Code == secondCode)
            .Select(x => x.Price).ToArrayAsync(token);
        Assert.Equal(2, prices.Length);
        Assert.True(prices.All(x => x == 11m) || prices.All(x => x == 12m));
    }

    [Fact]
    public async Task Preview_rejects_duplicate_unknown_malformed_and_partial_batches()
    {
        var token=TestContext.Current.CancellationToken;
        using var admin=factory.CreateClient(new WebApplicationFactoryClientOptions{HandleCookies=true});
        var csrf=await ApiTest.LoginAdmin(admin);
        var code="CSV-"+Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        var duplicate=await Preview(admin,csrf,Csv(Row(code),Row(code)));
        Assert.Contains(ApiTest.Property(duplicate,"errors").EnumerateArray(),x=>ApiTest.Property(x,"code").GetString()=="DUPLICATE_CODE");
        var unknown=await Preview(admin,csrf,Csv(Row(code,category:"Olmayan kategori")));
        Assert.Contains(ApiTest.Property(unknown,"errors").EnumerateArray(),x=>ApiTest.Property(x,"code").GetString()=="UNKNOWN_CATEGORY");
        var malformed=await Preview(admin,csrf,Header+"\n\""+code+",broken");
        Assert.Contains(ApiTest.Property(malformed,"errors").EnumerateArray(),x=>ApiTest.Property(x,"code").GetString()=="MALFORMED_CSV");

        var partialCsv=Csv(Row(code),Row(code+"B",price:"x"));
        var partial=await Preview(admin,csrf,partialCsv);
        Assert.False(ApiTest.Property(partial,"valid").GetBoolean());
        var apply=await ApiTest.SendJson(admin,HttpMethod.Post,"/api/admin/products/import/apply",new{csv=partialCsv,importId=Guid.NewGuid(),previewToken="00"},csrf);
        Assert.Equal(HttpStatusCode.Conflict,apply.StatusCode);
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        Assert.False(await db.Products.AnyAsync(x=>x.Code==code||x.Code==code+"B",token));
    }

    [Fact]
    public async Task Apply_checks_preview_rowversion_and_idempotency()
    {
        var token=TestContext.Current.CancellationToken;
        using var admin=factory.CreateClient(new WebApplicationFactoryClientOptions{HandleCookies=true});
        var csrf=await ApiTest.LoginAdmin(admin);
        var code="CSV-"+Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var csv=Csv(Row(code,stock:"7",reason:"İlk CSV stoğu"));
        var preview=await Preview(admin,csrf,csv);
        Assert.True(ApiTest.Property(preview,"valid").GetBoolean());
        var previewToken=ApiTest.Property(preview,"previewToken").GetString()!;

        var mismatch=await ApiTest.SendJson(admin,HttpMethod.Post,"/api/admin/products/import/apply",new{csv=Csv(Row(code,stock:"8",reason:"değişti")),importId=Guid.NewGuid(),previewToken},csrf);
        Assert.Equal(HttpStatusCode.Conflict,mismatch.StatusCode);

        var importId=Guid.NewGuid();
        var first=await ApiTest.SendJson(admin,HttpMethod.Post,"/api/admin/products/import/apply",new{csv,importId,previewToken},csrf);
        Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        var second=await ApiTest.SendJson(admin,HttpMethod.Post,"/api/admin/products/import/apply",new{csv,importId,previewToken},csrf);
        Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        Assert.True(ApiTest.Property(await second.Content.ReadFromJsonAsync<JsonElement>(token),"alreadyApplied").GetBoolean());

        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        var product=await db.Products.SingleAsync(x=>x.Code==code,token);
        Assert.Equal(1,await db.StockMovements.CountAsync(x=>x.ProductId==product.Id&&x.MovementType=="CsvImport",token));

        var exported=await admin.GetStringAsync("/api/admin/products/export",token);
        var version=exported.Split('\n').Single(x=>x.StartsWith(code+",",StringComparison.Ordinal)).Split(',')[12];
        var updateCsv=Csv(Row(code,stock:"9",version:version,reason:"CSV güncelleme"));
        var updatePreview=await Preview(admin,csrf,updateCsv);
        var updateToken=ApiTest.Property(updatePreview,"previewToken").GetString()!;
        product.Name="eşzamanlı değişiklik"; await db.SaveChangesAsync(token);
        var stale=await ApiTest.SendJson(admin,HttpMethod.Post,"/api/admin/products/import/apply",new{csv=updateCsv,importId=Guid.NewGuid(),previewToken=updateToken},csrf);
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        Assert.Equal("IMPORT_PREVIEW_STALE",ApiTest.Property(await stale.Content.ReadFromJsonAsync<JsonElement>(token),"code").GetString());
    }

    [Fact]
    public async Task Export_escapes_formula_like_text()
    {
        var token=TestContext.Current.CancellationToken;
        using var admin=factory.CreateClient(new WebApplicationFactoryClientOptions{HandleCookies=true});
        await ApiTest.LoginAdmin(admin);
        using var scope=factory.Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<BusinessDbContext>();
        var p=new U1.Business.Domain.Product{Code="CSV-"+Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),Name="=2+2",Description="@value",Brand="+brand",ManufacturerCode="-maker",ImageUrl="/images/product.svg",Stock=0,CriticalStock=1,Price=1m,CategoryId=1};
        db.Products.Add(p); await db.SaveChangesAsync(token);
        var csv=await admin.GetStringAsync("/api/admin/products/export",token);
        var line=csv.Split('\n').Single(x=>x.StartsWith(p.Code+",",StringComparison.Ordinal));
        Assert.Contains("'=2+2",line); Assert.Contains("'@value",line); Assert.Contains("'+brand",line); Assert.Contains("'-maker",line);
    }

    private static async Task<JsonElement> Preview(HttpClient client,string csrf,string csv){var response=await ApiTest.SendJson(client,HttpMethod.Post,"/api/admin/products/import/preview",new{csv},csrf);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);}
    private static string Csv(params string[] rows)=>Header+"\n"+string.Join("\n",rows);
    private static string Row(string code,string stock="0",string price="10.00",string category="Diagnostik cihazlar",string version="",string reason="")=>string.Join(",",code,"CSV ürün","CSV açıklama","CSV","CSV-M","","","/images/product.svg",stock,"2",price,category,version,reason);
}
