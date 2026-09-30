using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Storage;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private const int CsvMaxBytes = 1048576;
    private const int CsvMaxRows = 1000;
    private static readonly string[] CsvHeaders = ["code","name","description","brand","manufacturerCode","specialCode1","specialCode2","imageUrl","stock","criticalStock","price","category","rowVersion","stockReason"];

    private static void MapProductCsv(RouteGroupBuilder api)
    {
        api.MapGet("/products/export", async (BusinessDbContext db, HttpContext c) =>
        {
            var rows = await (from p in db.Products.AsNoTracking()
                              join category in db.Categories.AsNoTracking() on p.CategoryId equals category.Id
                              orderby p.Code select new { p, category.Name }).ToListAsync(c.RequestAborted);
            var csv = new StringBuilder().AppendLine(string.Join(",", CsvHeaders));
            foreach (var x in rows)
            {
                var p=x.p;
                csv.AppendLine(string.Join(",", [Cell(p.Code),Cell(p.Name),Cell(p.Description),Cell(p.Brand),Cell(p.ManufacturerCode),Cell(p.SpecialCode1),Cell(p.SpecialCode2),Cell(p.ImageUrl),p.Stock.ToString(CultureInfo.InvariantCulture),p.CriticalStock.ToString(CultureInfo.InvariantCulture),p.Price.ToString("0.00",CultureInfo.InvariantCulture),Cell(x.Name),Convert.ToBase64String(p.RowVersion),""]));
            }
            return Results.Text(csv.ToString(),"text/csv; charset=utf-8",Encoding.UTF8);
        });

        api.MapPost("/products/import/preview", async (CsvPreviewInput input, BusinessDbContext db, HttpContext c) =>
        {
            var check=await ValidateCsv(input?.Csv,db,c.RequestAborted);
            return Results.Ok(new { valid=check.Errors.Count==0, rows=check.Rows.Count, creates=check.Rows.Count(x=>x.ProductId is null), updates=check.Rows.Count(x=>x.ProductId is not null), errors=check.Errors, previewToken=check.Errors.Count==0 ? PreviewHash(input!.Csv,check.Rows) : null });
        });

        api.MapPost("/products/import/apply", async (CsvApplyInput input, BusinessDbContext db, HttpContext c) =>
        {
            if(input is null || input.ImportId==Guid.Empty || string.IsNullOrWhiteSpace(input.PreviewToken))
                throw new BusinessException("Geçerli importId ve previewToken gerekli.");
            await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,c.RequestAborted);
            var importResource="U1Business.ProductImport."+input.ImportId.ToString("N");
            var lockResult=await SqlApplicationLock.AcquireAsync(
                (SqlConnection)db.Database.GetDbConnection(),
                (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction(),
                importResource,"Transaction",5000);
            if(lockResult<0) throw new BusinessException("Aynı toplu aktarım başka bir işlemde çalışıyor; tekrar deneyin.",409,"IMPORT_BUSY");
            var marker="CSV import "+input.ImportId.ToString("N");
            if(await db.AdminEvents.AsNoTracking().AnyAsync(x=>x.EventType=="ProductImportApplied"&&x.Summary==marker,c.RequestAborted))
            {
                await tx.CommitAsync(c.RequestAborted);
                return Results.Ok(new { alreadyApplied=true });
            }
            var check=await ValidateCsv(input.Csv,db,c.RequestAborted,true);
            if(check.Errors.Count!=0) throw new BusinessException("CSV artık geçerli değil; yeniden önizleyin.",409,"IMPORT_PREVIEW_STALE");
            var hash=PreviewHash(input.Csv,check.Rows);
            if(!string.Equals(hash,input.PreviewToken,StringComparison.Ordinal))
                throw new BusinessException("CSV veya ürünler önizlemeden sonra değişti; yeniden önizleyin.",409,"IMPORT_PREVIEW_STALE");

            var actor=c.UserId();
            foreach(var row in check.Rows)
            {
                Product product;
                var oldStock=0;
                var created=row.ProductId is null;
                if(created){ product=new Product(); db.Products.Add(product); }
                else
                {
                    product=await db.Products.SingleAsync(x=>x.Id==row.ProductId!.Value,c.RequestAborted);
                    oldStock=product.Stock;
                    db.Entry(product).Property(x=>x.RowVersion).OriginalValue=row.CurrentVersion!;
                }
                ApplyRow(row,product);
                await db.SaveChangesAsync(c.RequestAborted);
                var audit=AuditTrail.Event(actor,created?"ProductImported":"ProductImportUpdated","Product",product.Id,$"{product.Code} kodlu ürün CSV toplu aktarımıyla güncellendi.");
                db.AdminEvents.Add(audit);
                await db.SaveChangesAsync(c.RequestAborted);
                if(oldStock!=product.Stock)
                {
                    db.StockMovements.Add(AuditTrail.Stock(product.Id,oldStock,product.Stock,"CsvImport",row.StockReason,actor,adminEventId:audit.Id));
                    await db.SaveChangesAsync(c.RequestAborted);
                }
            }
            db.AdminEvents.Add(AuditTrail.Event(actor,"ProductImportApplied","ProductImport",null,marker));
            await db.SaveChangesAsync(c.RequestAborted);
            await tx.CommitAsync(c.RequestAborted);
            return Results.Ok(new { alreadyApplied=false, applied=check.Rows.Count });
        });
    }

    private static async Task<CsvCheck> ValidateCsv(string? csv,BusinessDbContext db,CancellationToken token,bool locked=false)
    {
        var result=new CsvCheck();
        if(string.IsNullOrWhiteSpace(csv)){result.Errors.Add(new(0,"EMPTY_FILE","CSV içeriği boş."));return result;}
        if(Encoding.UTF8.GetByteCount(csv)>CsvMaxBytes){result.Errors.Add(new(0,"FILE_TOO_LARGE","CSV en fazla 1 MiB olabilir."));return result;}
        List<string[]> records;
        try{records=ParseCsv(csv);}catch(FormatException ex){result.Errors.Add(new(0,"MALFORMED_CSV",ex.Message));return result;}
        if(records.Count==0 || !records[0].SequenceEqual(CsvHeaders,StringComparer.Ordinal)){result.Errors.Add(new(1,"INVALID_HEADER","CSV başlıkları belgelenen biçimle eşleşmeli."));return result;}
        if(records.Count-1>CsvMaxRows){result.Errors.Add(new(0,"TOO_MANY_ROWS","CSV en fazla 1000 veri satırı içerebilir."));return result;}
        var categories=await db.Categories.AsNoTracking().ToListAsync(token);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(var i=1;i<records.Count;i++)
        {
            var f=records[i]; var line=i+1;
            if(f.Length!=CsvHeaders.Length){result.Errors.Add(new(line,"COLUMN_COUNT","14 sütun bekleniyor."));continue;}
            var code=Unescape(f[0]).Trim();
            if(!seen.Add(code)){result.Errors.Add(new(line,"DUPLICATE_CODE","Aynı ürün kodu dosyada birden fazla kez bulunamaz."));continue;}
            Product? existing;
            if(locked) existing=await db.Products.FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Code={code}").SingleOrDefaultAsync(token);
            else existing=await db.Products.AsNoTracking().SingleOrDefaultAsync(x=>x.Code==code,token);
            var category=categories.SingleOrDefault(x=>string.Equals(x.Name,Unescape(f[11]).Trim(),StringComparison.OrdinalIgnoreCase));
            if(category is null){result.Errors.Add(new(line,"UNKNOWN_CATEGORY","Kategori bulunamadı."));continue;}
            if(!int.TryParse(f[8],NumberStyles.None,CultureInfo.InvariantCulture,out var stock)||stock<0){result.Errors.Add(new(line,"INVALID_STOCK","Stok geçersiz."));continue;}
            if(!int.TryParse(f[9],NumberStyles.None,CultureInfo.InvariantCulture,out var critical)||critical<0||critical>1000000){result.Errors.Add(new(line,"INVALID_CRITICAL_STOCK","Kritik stok geçersiz."));continue;}
            if(!decimal.TryParse(f[10],NumberStyles.Number,CultureInfo.InvariantCulture,out var price)||price<0.01m||price>99999999m||decimal.Round(price,2)!=price){result.Errors.Add(new(line,"INVALID_PRICE","Fiyat geçersiz."));continue;}
            if(existing is not null)
            {
                byte[]? supplied=null; try{supplied=Convert.FromBase64String(f[12]);}catch(FormatException){}
                if(supplied is null||supplied.Length!=8||!supplied.AsSpan().SequenceEqual(existing.RowVersion)){result.Errors.Add(new(line,"STALE_ROWVERSION","Mevcut ürün rowVersion değeri güncel değil."));continue;}
            }
            else if(!string.IsNullOrWhiteSpace(f[12])){result.Errors.Add(new(line,"ROWVERSION_FOR_NEW","Yeni üründe rowVersion boş olmalı."));continue;}
            var reason=Unescape(f[13]).Trim();
            if((existing?.Stock??0)!=stock && string.IsNullOrWhiteSpace(reason)){result.Errors.Add(new(line,"STOCK_REASON_REQUIRED","Stok değişikliği için stockReason gerekli."));continue;}
            var row=new CsvRow(existing?.Id,code,Unescape(f[1]).Trim(),Unescape(f[2]).Trim(),Unescape(f[3]).Trim(),Unescape(f[4]).Trim(),Unescape(f[5]).Trim(),Unescape(f[6]).Trim(),Unescape(f[7]).Trim(),stock,critical,price,category.Id,existing?.RowVersion,reason);
            var input=new ProductInput{Code=row.Code,Name=row.Name,Description=row.Description,Brand=row.Brand,ManufacturerCode=row.ManufacturerCode,SpecialCode1=row.SpecialCode1,SpecialCode2=row.SpecialCode2,ImageUrl=row.ImageUrl,Stock=row.Stock,CriticalStock=row.CriticalStock,Price=row.Price,CategoryId=row.CategoryId};
            var errors=new List<ValidationResult>();
            if(!Validator.TryValidateObject(input,new ValidationContext(input),errors,true)){result.Errors.Add(new(line,"INVALID_PRODUCT",errors[0].ErrorMessage??"Ürün alanları geçersiz."));continue;}
            if(string.IsNullOrWhiteSpace(row.ImageUrl)||!(row.ImageUrl.StartsWith("/images/")||row.ImageUrl.StartsWith("/uploads/")||Uri.TryCreate(row.ImageUrl,UriKind.Absolute,out var uri)&&uri.Scheme=="https")){result.Errors.Add(new(line,"INVALID_IMAGE","Görsel adresi geçersiz."));continue;}
            result.Rows.Add(row);
        }
        return result;
    }

    private static void ApplyRow(CsvRow r,Product p){p.Code=r.Code;p.Name=r.Name;p.Description=r.Description;p.Brand=r.Brand;p.ManufacturerCode=r.ManufacturerCode;p.SpecialCode1=r.SpecialCode1;p.SpecialCode2=r.SpecialCode2;p.ImageUrl=r.ImageUrl;p.Stock=r.Stock;p.CriticalStock=r.CriticalStock;p.Price=r.Price;p.CategoryId=r.CategoryId;}
    private static string PreviewHash(string csv,IEnumerable<CsvRow> rows){var b=new StringBuilder(csv.Replace("\r\n","\n"));foreach(var r in rows.OrderBy(x=>x.Code,StringComparer.OrdinalIgnoreCase))b.Append('\n').Append(r.Code).Append(':').Append(r.CurrentVersion is null?"NEW":Convert.ToBase64String(r.CurrentVersion));return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(b.ToString())));}
    private static string Cell(string value){value??="";if(value.Length>0&&"=+-@".Contains(value[0]))value="'"+value;return value.IndexOfAny([',','"','\r','\n'])>=0?"\""+value.Replace("\"","\"\"")+"\"":value;}
    private static string Unescape(string value)=>value.Length>1&&value[0]=='\''&&"=+-@".Contains(value[1])?value[1..]:value;
    private static List<string[]> ParseCsv(string text){var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();var quoted=false;for(var i=0;i<text.Length;i++){var ch=text[i];if(quoted){if(ch=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else quoted=false;}else field.Append(ch);continue;}if(ch=='"'){if(field.Length!=0)throw new FormatException("Çift tırnak alanın yalnız başında kullanılabilir.");quoted=true;}else if(ch==','){row.Add(field.ToString());field.Clear();}else if(ch=='\r'||ch=='\n'){if(ch=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;row.Add(field.ToString());field.Clear();if(row.Count!=1||row[0].Length!=0)rows.Add(row.ToArray());row.Clear();}else field.Append(ch);}if(quoted)throw new FormatException("Kapanmamış çift tırnak bulundu.");row.Add(field.ToString());if(row.Count!=1||row[0].Length!=0)rows.Add(row.ToArray());return rows;}

    public class CsvPreviewInput{public string Csv{get;set;}="";}
    public sealed class CsvApplyInput:CsvPreviewInput{public Guid ImportId{get;set;} public string PreviewToken{get;set;}="";}
    private sealed class CsvCheck{public List<CsvRow> Rows{get;}=[];public List<CsvError> Errors{get;}=[];}
    private sealed record CsvError(int Row,string Code,string Message);
    private sealed record CsvRow(int? ProductId,string Code,string Name,string Description,string Brand,string ManufacturerCode,string SpecialCode1,string SpecialCode2,string ImageUrl,int Stock,int CriticalStock,decimal Price,int CategoryId,byte[]? CurrentVersion,string StockReason);
}
