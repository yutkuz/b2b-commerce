using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static async Task<CsvCheck> ValidateCsv(
        string? csv,
        BusinessDbContext db,
        ProductImageStorage storage,
        CancellationToken token,
        bool locked = false)
    {
        var result = new CsvCheck();
        if (string.IsNullOrWhiteSpace(csv))
        {
            result.Errors.Add(new(0, "EMPTY_FILE", "CSV içeriği boş."));
            return result;
        }
        if (Encoding.UTF8.GetByteCount(csv) > ProductCsvFormat.MaxBytes)
        {
            result.Errors.Add(new(0, "FILE_TOO_LARGE", "CSV en fazla 1 MiB olabilir."));
            return result;
        }

        List<string[]> records;
        try
        {
            records = ProductCsvFormat.Parse(csv);
        }
        catch (FormatException exception)
        {
            result.Errors.Add(new(0, "MALFORMED_CSV", exception.Message));
            return result;
        }
        if (records.Count == 0
            || !records[0].SequenceEqual(ProductCsvFormat.Headers, StringComparer.Ordinal))
        {
            result.Errors.Add(new(1, "INVALID_HEADER", "CSV başlıkları belgelenen biçimle eşleşmeli."));
            return result;
        }
        if (records.Count - 1 > ProductCsvFormat.MaxRows)
        {
            result.Errors.Add(new(0, "TOO_MANY_ROWS", "CSV en fazla 1000 veri satırı içerebilir."));
            return result;
        }
        if (records.Count == 1)
        {
            result.Errors.Add(new(0, "EMPTY_BATCH", "CSV en az bir ürün satırı içermeli."));
            return result;
        }

        var categories = await db.Categories.AsNoTracking().ToListAsync(token);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        if (locked)
        {
            foreach (var code in records.Skip(1)
                         .Where(x => x.Length == ProductCsvFormat.Headers.Length)
                         .Select(x => ProductCsvFormat.ImportCell(x[0]).Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var product = await db.Products
                    .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Code={code}")
                    .SingleOrDefaultAsync(token);
                if (product is not null)
                    existingProducts[code] = product;
            }
        }

        for (var index = 1; index < records.Count; index++)
        {
            var fields = records[index];
            var line = index + 1;
            if (fields.Length != ProductCsvFormat.Headers.Length)
            {
                result.Errors.Add(new(line, "COLUMN_COUNT", "14 sütun bekleniyor."));
                continue;
            }

            var code = ProductCsvFormat.ImportCell(fields[0]).Trim();
            if (!seen.Add(code))
            {
                result.Errors.Add(new(line, "DUPLICATE_CODE", "Aynı ürün kodu dosyada birden fazla kez bulunamaz."));
                continue;
            }

            Product? existing = locked
                ? existingProducts.GetValueOrDefault(code)
                : await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code, token);
            var categoryName = ProductCsvFormat.ImportCell(fields[11]).Trim();
            var category = categories.SingleOrDefault(x => string.Equals(
                x.Name,
                categoryName,
                StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                result.Errors.Add(new(line, "UNKNOWN_CATEGORY", "Kategori bulunamadı."));
                continue;
            }
            if (!int.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out var stock)
                || stock < 0)
            {
                result.Errors.Add(new(line, "INVALID_STOCK", "Stok geçersiz."));
                continue;
            }
            if (!int.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out var critical)
                || critical is < 0 or > 1000000)
            {
                result.Errors.Add(new(line, "INVALID_CRITICAL_STOCK", "Kritik stok geçersiz."));
                continue;
            }
            if (!decimal.TryParse(fields[10], NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
                || price is < 0.01m or > 99999999m
                || decimal.Round(price, 2) != price)
            {
                result.Errors.Add(new(line, "INVALID_PRICE", "Fiyat geçersiz."));
                continue;
            }

            if (existing is not null)
            {
                byte[]? version = null;
                try { version = Convert.FromBase64String(fields[12]); }
                catch (FormatException) { }
                if (version is null || version.Length != 8
                    || !version.AsSpan().SequenceEqual(existing.RowVersion))
                {
                    result.Errors.Add(new(line, "STALE_ROWVERSION", "Mevcut ürün rowVersion değeri güncel değil."));
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(fields[12]))
            {
                result.Errors.Add(new(line, "ROWVERSION_FOR_NEW", "Yeni üründe rowVersion boş olmalı."));
                continue;
            }

            var reason = ProductCsvFormat.ImportCell(fields[13]).Trim();
            if ((existing?.Stock ?? 0) != stock && string.IsNullOrWhiteSpace(reason))
            {
                result.Errors.Add(new(line, "STOCK_REASON_REQUIRED", "Stok değişikliği için stockReason gerekli."));
                continue;
            }

            var row = new CsvRow(
                line,
                existing?.Id,
                code,
                ProductCsvFormat.ImportCell(fields[1]).Trim(),
                ProductCsvFormat.ImportCell(fields[2]).Trim(),
                ProductCsvFormat.ImportCell(fields[3]).Trim(),
                ProductCsvFormat.ImportCell(fields[4]).Trim(),
                ProductCsvFormat.ImportCell(fields[5]).Trim(),
                ProductCsvFormat.ImportCell(fields[6]).Trim(),
                ProductCsvFormat.ImportCell(fields[7]).Trim(),
                stock,
                critical,
                price,
                category.Id,
                category.Name,
                existing?.RowVersion,
                reason,
                existing is null
                    ? null
                    : Snapshot(existing, categories.Single(x => x.Id == existing.CategoryId).Name));
            var productInput = new ProductInput
            {
                Code = row.Code,
                Name = row.Name,
                Description = row.Description,
                Brand = row.Brand,
                ManufacturerCode = row.ManufacturerCode,
                SpecialCode1 = row.SpecialCode1,
                SpecialCode2 = row.SpecialCode2,
                ImageUrl = row.ImageUrl,
                Stock = row.Stock,
                CriticalStock = row.CriticalStock,
                Price = row.Price,
                CategoryId = row.CategoryId
            };
            var validationErrors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(
                    productInput,
                    new ValidationContext(productInput),
                    validationErrors,
                    true))
            {
                result.Errors.Add(new(line, "INVALID_PRODUCT",
                    validationErrors[0].ErrorMessage ?? "Ürün alanları geçersiz."));
                continue;
            }
            if (!ValidImageUrl(row.ImageUrl))
            {
                result.Errors.Add(new(line, "INVALID_IMAGE", "Görsel adresi geçersiz."));
                continue;
            }
            try
            {
                await storage.EnsureManagedReferenceExistsAsync(row.ImageUrl, token);
            }
            catch (BusinessException exception) when (exception.Code is "IMAGE_CHANGED" or "IMAGE_PATH_INVALID")
            {
                result.Errors.Add(new(line, "IMAGE_NOT_FOUND", "Yüklenen görsel artık bulunamıyor."));
                continue;
            }

            result.Rows.Add(row);
        }
        return result;
    }

    private static bool ValidImageUrl(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.StartsWith("/images/", StringComparison.Ordinal)
            || value.StartsWith("/uploads/", StringComparison.Ordinal)
            || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https");

    private static void ApplyRow(CsvRow row, Product product)
    {
        product.Code = row.Code;
        product.Name = row.Name;
        product.Description = row.Description;
        product.Brand = row.Brand;
        product.ManufacturerCode = row.ManufacturerCode;
        product.SpecialCode1 = row.SpecialCode1;
        product.SpecialCode2 = row.SpecialCode2;
        product.ImageUrl = row.ImageUrl;
        product.Stock = row.Stock;
        product.CriticalStock = row.CriticalStock;
        product.Price = row.Price;
        product.CategoryId = row.CategoryId;
    }

    private static CsvSnapshot Snapshot(CsvRow row) => new(
        row.Name, row.Description, row.Brand, row.ManufacturerCode,
        row.SpecialCode1, row.SpecialCode2, row.ImageUrl,
        row.Stock, row.CriticalStock, row.Price, row.CategoryName);

    private static CsvSnapshot Snapshot(Product product, string categoryName) => new(
        product.Name, product.Description, product.Brand, product.ManufacturerCode,
        product.SpecialCode1, product.SpecialCode2, product.ImageUrl,
        product.Stock, product.CriticalStock, product.Price, categoryName);

    private static string PreviewHash(string csv, IEnumerable<CsvRow> rows)
    {
        var text = new StringBuilder(csv.Replace("\r\n", "\n"));
        foreach (var row in rows.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            text.Append('\n').Append(row.Code).Append(':')
                .Append(row.CurrentVersion is null
                    ? "NEW"
                    : Convert.ToBase64String(row.CurrentVersion))
                .Append(':').Append(row.CategoryId);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string ContentHash(string csv) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            csv.Replace("\r\n", "\n"))));

    private static BusinessException PreviewStale() =>
        new("CSV veya ürünler önizlemeden sonra değişti; yeniden önizleyin.",
            409, "IMPORT_PREVIEW_STALE");

    public class CsvPreviewInput
    {
        public string Csv { get; set; } = "";
    }

    public sealed class CsvApplyInput : CsvPreviewInput
    {
        public Guid ImportId { get; set; }
        public string PreviewToken { get; set; } = "";
    }

    private sealed class CsvCheck
    {
        public List<CsvRow> Rows { get; } = [];
        public List<CsvError> Errors { get; } = [];
    }

    private sealed record CsvError(int Row, string Code, string Message);

    private sealed record CsvSnapshot(
        string Name, string Description, string Brand, string ManufacturerCode,
        string SpecialCode1, string SpecialCode2, string ImageUrl,
        int Stock, int CriticalStock, decimal Price, string Category);

    private sealed record CsvRow(
        int Line, int? ProductId, string Code, string Name, string Description,
        string Brand, string ManufacturerCode, string SpecialCode1, string SpecialCode2,
        string ImageUrl, int Stock, int CriticalStock, decimal Price,
        int CategoryId, string CategoryName, byte[]? CurrentVersion,
        string StockReason, CsvSnapshot? Before);
}
