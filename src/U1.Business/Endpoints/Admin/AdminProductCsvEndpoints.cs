using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapProductCsv(RouteGroupBuilder api)
    {
        api.MapGet("/products/export", ExportProducts);
        api.MapPost("/products/import/preview", PreviewImport);
        api.MapPost("/products/import/apply", ApplyImport);
    }

    private static async Task<IResult> ExportProducts(BusinessDbContext db, HttpContext context)
    {
        var products = await (
            from product in db.Products.AsNoTracking()
            join category in db.Categories.AsNoTracking()
                on product.CategoryId equals category.Id
            orderby product.Code
            select new { Product = product, CategoryName = category.Name }
        ).ToListAsync(context.RequestAborted);

        var csv = new StringBuilder().AppendLine(string.Join(",", ProductCsvFormat.Headers));
        foreach (var item in products)
        {
            var product = item.Product;
            csv.AppendLine(string.Join(",", new[]
            {
                ProductCsvFormat.ExportCell(product.Code),
                ProductCsvFormat.ExportCell(product.Name),
                ProductCsvFormat.ExportCell(product.Description),
                ProductCsvFormat.ExportCell(product.Brand),
                ProductCsvFormat.ExportCell(product.ManufacturerCode),
                ProductCsvFormat.ExportCell(product.SpecialCode1),
                ProductCsvFormat.ExportCell(product.SpecialCode2),
                ProductCsvFormat.ExportCell(product.ImageUrl),
                product.Stock.ToString(CultureInfo.InvariantCulture),
                product.CriticalStock.ToString(CultureInfo.InvariantCulture),
                product.Price.ToString("0.00", CultureInfo.InvariantCulture),
                ProductCsvFormat.ExportCell(item.CategoryName),
                Convert.ToBase64String(product.RowVersion),
                ""
            }));
        }

        return Results.File(
            Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv; charset=utf-8",
            "u1-products.csv");
    }

    private static async Task<IResult> PreviewImport(
        CsvPreviewInput? input,
        BusinessDbContext db,
        ProductImageStorage storage,
        HttpContext context)
    {
        var check = await ValidateCsv(input?.Csv, db, storage, context.RequestAborted);
        return Results.Ok(new
        {
            valid = check.Errors.Count == 0,
            rows = check.Rows.Count,
            creates = check.Rows.Count(x => x.ProductId is null),
            updates = check.Rows.Count(x => x.ProductId is not null),
            changes = check.Rows.Select(x => new
            {
                row = x.Line,
                x.Code,
                action = x.ProductId is null ? "create" : "update",
                before = x.Before,
                after = Snapshot(x)
            }),
            errors = check.Errors,
            previewToken = check.Errors.Count == 0
                ? PreviewHash(input!.Csv, check.Rows)
                : null
        });
    }

    private static async Task<IResult> ApplyImport(
        CsvApplyInput? input,
        BusinessDbContext db,
        ProductImageStorage storage,
        HttpContext context)
    {
        if (input is null || input.ImportId == Guid.Empty
            || string.IsNullOrWhiteSpace(input.PreviewToken))
            throw new BusinessException("Geçerli importId ve previewToken gerekli.");

        var token = context.RequestAborted;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                token);
            var resource = "U1Business.ProductImport." + input.ImportId.ToString("N");
            var lockResult = await SqlApplicationLock.AcquireAsync(
                (SqlConnection)db.Database.GetDbConnection(),
                (SqlTransaction)transaction.GetDbTransaction(),
                resource,
                "Transaction",
                5000);
            if (lockResult < 0)
                throw new BusinessException(
                    "Aynı toplu aktarım başka bir işlemde çalışıyor; tekrar deneyin.",
                    409,
                    "IMPORT_BUSY");

            await ImageReferenceGuard.AcquireAsync(db, token);

            var markerPrefix = "CSV import " + input.ImportId.ToString("N") + " ";
            var marker = markerPrefix + ContentHash(input.Csv);
            var appliedMarker = await db.AdminEvents.AsNoTracking()
                .Where(x => x.EventType == "ProductImportApplied"
                    && x.Summary.StartsWith(markerPrefix))
                .Select(x => x.Summary)
                .SingleOrDefaultAsync(token);
            if (appliedMarker is not null)
            {
                if (!string.Equals(appliedMarker, marker, StringComparison.Ordinal))
                    throw new BusinessException(
                        "Bu importId farklı bir CSV için kullanıldı. Yeni bir importId seçin.",
                        409,
                        "IMPORT_ID_REUSED");
                await transaction.CommitAsync(token);
                return Results.Ok(new { alreadyApplied = true });
            }

            var check = await ValidateCsv(input.Csv, db, storage, token, locked: true);
            if (check.Errors.Count > 0)
                throw PreviewStale();
            if (!string.Equals(
                    PreviewHash(input.Csv, check.Rows),
                    input.PreviewToken,
                    StringComparison.Ordinal))
                throw PreviewStale();

            var actor = context.UserId();
            foreach (var row in check.Rows.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
            {
                var created = row.ProductId is null;
                var product = created
                    ? new Product()
                    : await db.Products.SingleAsync(x => x.Id == row.ProductId!.Value, token);
                var oldStock = product.Stock;
                if (created)
                    db.Products.Add(product);
                else
                    db.Entry(product).Property(x => x.RowVersion).OriginalValue = row.CurrentVersion!;

                ApplyRow(row, product);
                await db.SaveChangesAsync(token);

                var audit = AuditTrail.Event(
                    actor,
                    created ? "ProductImported" : "ProductImportUpdated",
                    "Product",
                    product.Id,
                    $"{product.Code} kodlu ürün CSV toplu aktarımıyla güncellendi.");
                db.AdminEvents.Add(audit);
                await db.SaveChangesAsync(token);
                if (oldStock != product.Stock)
                {
                    db.StockMovements.Add(AuditTrail.Stock(
                        product.Id,
                        oldStock,
                        product.Stock,
                        "CsvImport",
                        row.StockReason,
                        actor,
                        adminEventId: audit.Id));
                    await db.SaveChangesAsync(token);
                }
            }

            db.AdminEvents.Add(AuditTrail.Event(
                actor,
                "ProductImportApplied",
                "ProductImport",
                null,
                marker));
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Results.Ok(new { alreadyApplied = false, applied = check.Rows.Count });
        }
        catch (DbUpdateConcurrencyException)
        {
            throw PreviewStale();
        }
        catch (SqlException exception) when (exception.Number is 1205 or -2)
        {
            throw new BusinessException(
                "CSV aktarımı şu anda tamamlanamadı. Güncel önizlemeyi alıp tekrar deneyin.",
                503,
                "IMPORT_RETRY");
        }
    }

}
