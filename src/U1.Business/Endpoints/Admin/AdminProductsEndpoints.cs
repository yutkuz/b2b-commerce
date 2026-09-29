using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapProducts(RouteGroupBuilder api)
    {
        api.MapPost(
            "/products",
            async (ProductInput input, BusinessDbContext db, HttpContext c) =>
            {
                await ValidateProduct(input, db);
                await using var tx = await db.Database.BeginTransactionAsync();
                var product = ToProduct(input);
                db.Products.Add(product);
                await db.SaveChangesAsync();
                var audit = AuditTrail.Event(
                    c.UserId(),
                    "ProductCreated",
                    "Product",
                    product.Id,
                    $"{product.Code} kodlu ürün oluşturuldu; fiyat {product.Price:0.00}, stok {product.Stock}.");
                db.AdminEvents.Add(audit);
                await db.SaveChangesAsync();
                db.StockMovements.Add(AuditTrail.Stock(
                    product.Id,
                    0,
                    product.Stock,
                    "ProductCreated",
                    "Ürün oluşturulurken girilen başlangıç stoğu.",
                    c.UserId(),
                    adminEventId: audit.Id));
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(new { id = product.Id });
            }
        );

        api.MapPut(
            "/products/{id:int}",
            async (int id, ProductUpdateInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Ürün bilgileri gerekli.");
                if (string.IsNullOrWhiteSpace(input.Version))
                    throw new BusinessException("Ürün sürümü gerekli.");

                byte[] version;
                try
                {
                    version = Convert.FromBase64String(input.Version);
                }
                catch (FormatException)
                {
                    throw new BusinessException("Ürün sürümü geçersiz.");
                }

                if (version.Length != 8)
                    throw new BusinessException("Ürün sürümü geçersiz.");

                await ValidateProduct(input, db);
                await using var tx = await db.Database.BeginTransactionAsync();
                var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id)
                    ?? throw new BusinessException("Ürün bulunamadı.", 404);
                var previousStock = product.Stock;
                var previousPrice = product.Price;
                if (previousStock != input.Stock && string.IsNullOrWhiteSpace(input.StockReason))
                    throw new BusinessException("Stok değişikliği için neden girin.");

                db.Entry(product).Property(x => x.RowVersion).OriginalValue = version;
                Apply(input, product);

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    if (!await db.Products.AsNoTracking().AnyAsync(x => x.Id == id))
                        throw new BusinessException("Ürün bulunamadı.", 404);

                    throw new BusinessException(
                        "Ürün başka bir işlemde değişti. Girdilerinizi koruyarak güncel ürünü yeniden yükleyin.",
                        409,
                        "PRODUCT_CHANGED"
                    );
                }

                var summary = previousPrice == product.Price
                    ? $"{product.Code} kodlu ürün güncellendi."
                    : $"{product.Code} kodlu ürünün fiyatı {previousPrice:0.00} değerinden {product.Price:0.00} değerine güncellendi.";
                var audit = AuditTrail.Event(
                    c.UserId(),
                    previousPrice == product.Price ? "ProductUpdated" : "ProductPriceChanged",
                    "Product",
                    product.Id,
                    summary);
                db.AdminEvents.Add(audit);
                await db.SaveChangesAsync();

                if (previousStock != product.Stock)
                {
                    db.StockMovements.Add(AuditTrail.Stock(
                        product.Id,
                        previousStock,
                        product.Stock,
                        "ManualAdjustment",
                        input.StockReason!.Trim(),
                        c.UserId(),
                        adminEventId: audit.Id));
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
                return Results.Ok();
            }
        );

        api.MapPost(
            "/images",
            async (HttpRequest request, IWebHostEnvironment env) =>
            {
                if (!request.HasFormContentType || request.ContentLength > 5 * 1024 * 1024)
                    throw new BusinessException(
                        "En fazla 4 MB boyutunda PNG, JPEG veya WebP seçin."
                    );
                var form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
                var file = form.Files.GetFile("file");
                if (file is null || file.Length == 0 || file.Length > 4 * 1024 * 1024)
                    throw new BusinessException("Geçerli bir görsel seçin (en fazla 4 MB).");

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, request.HttpContext.RequestAborted);
                var bytes = ms.ToArray();
                var ext = ProductImageValidator.Validate(bytes);
                var name = Guid.NewGuid().ToString("N") + ext;
                var folder = Path.Combine(env.WebRootPath, "uploads");
                Directory.CreateDirectory(folder);
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes);
                return Results.Ok(new { url = "/uploads/" + name });
            }
        );
    }

    private static async Task ValidateProduct(ProductInput input, BusinessDbContext db)
    {
        if (input is null)
            throw new BusinessException("Ürün bilgileri gerekli.");
        input.SpecialCode1 ??= "";
        input.SpecialCode2 ??= "";
        Rules.Validate(input);

        if (decimal.Round(input.Price, 2) != input.Price)
            throw new BusinessException("Fiyat en fazla iki ondalık basamak içermeli.");

        if (
            string.IsNullOrWhiteSpace(input.ImageUrl)
            || !input.ImageUrl.StartsWith("/images/")
                && !input.ImageUrl.StartsWith("/uploads/")
                && !(
                    Uri.TryCreate(input.ImageUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == "https"
                )
        )
            throw new BusinessException("Görsel adresi bir yükleme yolu veya HTTPS adresi olmalı.");

        if (!await db.Categories.AsNoTracking().AnyAsync(x => x.Id == input.CategoryId))
            throw new BusinessException("Geçerli bir kategori seçin.");
    }

    private static Product ToProduct(ProductInput input)
    {
        var product = new Product();
        Apply(input, product);
        return product;
    }

    private static void Apply(ProductInput input, Product product)
    {
        product.Code = input.Code;
        product.Name = input.Name;
        product.Description = input.Description;
        product.Brand = input.Brand;
        product.ManufacturerCode = input.ManufacturerCode;
        product.SpecialCode1 = input.SpecialCode1;
        product.SpecialCode2 = input.SpecialCode2;
        product.ImageUrl = input.ImageUrl;
        product.Stock = input.Stock;
        product.CriticalStock = input.CriticalStock;
        product.Price = input.Price;
        product.CategoryId = input.CategoryId;
    }
}
