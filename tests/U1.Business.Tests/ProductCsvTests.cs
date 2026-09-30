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
