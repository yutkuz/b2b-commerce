using System.Data;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapProducts(RouteGroupBuilder api)
    {
        api.MapGet(
            "/products",
            async (string? q, bool? archived, int? page, BusinessDbContext db) =>
            {
                var pageNumber = Math.Clamp(page ?? 1, 1, 100000);
                q = (q ?? "").Trim();
                if (q.Length > 200)
                    throw new BusinessException("Arama metni en fazla 200 karakter olabilir.");

                var query =
                    from product in db.Products.AsNoTracking()
                    join category in db.Categories.AsNoTracking()
                        on product.CategoryId equals category.Id
                    select new { Product = product, Category = category.Name };

                if (q.Length > 0)
                {
                    var search = "%"
                        + q.Replace("~", "~~")
                            .Replace("%", "~%")
                            .Replace("_", "~_")
                            .Replace("[", "~[")
                        + "%";

                    query = query.Where(x =>
                        EF.Functions.Like(x.Product.Name, search, "~")
                        || EF.Functions.Like(x.Product.Code, search, "~")
                        || EF.Functions.Like(x.Product.Brand, search, "~")
                        || EF.Functions.Like(x.Category, search, "~"));
                }

                if (archived is not null)
                    query = query.Where(x => x.Product.IsArchived == archived.Value);

                var total = await query.CountAsync();
                var items = await query
                    .OrderBy(x => x.Product.IsArchived)
                    .ThenBy(x => x.Product.Id)
                    .Skip((pageNumber - 1) * 20)
                    .Take(20)
                    .Select(x => new
                    {
                        x.Product.Id,
                        x.Product.Code,
                        x.Product.Name,
                        x.Product.Brand,
                        x.Product.ImageUrl,
                        x.Product.Stock,
                        x.Product.CriticalStock,
                        x.Product.Price,
                        x.Product.CategoryId,
                        x.Product.IsArchived,
                        x.Product.ArchivedAt,
                        x.Product.ArchiveReason,
                        x.Product.RowVersion,
                        x.Category
                    })
                    .ToListAsync();

                return new { items, total, page = pageNumber, pageSize = 20 };
            }
        );

        api.MapGet(
            "/products/{id:int}",
            async (int id, BusinessDbContext db) =>
            {
                var product = await (
                    from p in db.Products.AsNoTracking()
                    join category in db.Categories.AsNoTracking()
                        on p.CategoryId equals category.Id
                    where p.Id == id
                    select new
                    {
                        p.Id,
                        p.Code,
                        p.Name,
                        p.Description,
                        p.Brand,
                        p.ManufacturerCode,
                        p.SpecialCode1,
                        p.SpecialCode2,
                        p.ImageUrl,
                        p.Stock,
                        p.CriticalStock,
                        p.Price,
                        p.CategoryId,
                        p.CreatedAt,
                        p.IsArchived,
                        p.ArchivedAt,
                        p.ArchivedByUserId,
                        p.ArchiveReason,
                        p.RowVersion,
                        Category = category.Name
                    }).SingleOrDefaultAsync();

                return product ?? throw new BusinessException("Ürün bulunamadı.", 404);
            }
        );

        api.MapPost(
            "/products",
            async (ProductInput input, BusinessDbContext db, HttpContext c) =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await ValidateProduct(input, db);
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

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await ValidateProduct(input, db);
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

                    throw ProductChanged();
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
            "/products/{id:int}/archive",
            async (int id, ProductArchiveInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null || string.IsNullOrWhiteSpace(input.Reason))
                    throw new BusinessException("Arşivleme nedeni gerekli.");
                Rules.Validate(input);
                EnsureRowVersion(input.RowVersion, "Ürün sürümü geçersiz.");

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var product = await LockProductForWrite(db, id)
                    ?? throw new BusinessException("Ürün bulunamadı.", 404);
                EnsureProductVersion(product, input.RowVersion);

                if (product.IsArchived)
                {
                    await tx.CommitAsync();
                    return Results.Ok();
                }

                product.IsArchived = true;
                product.ArchivedAt = DateTime.UtcNow;
                product.ArchivedByUserId = c.UserId();
                product.ArchiveReason = input.Reason.Trim();
                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "ProductArchived",
                    "Product",
                    product.Id,
                    $"{product.Code} kodlu ürün arşivlendi. Neden: {product.ArchiveReason}"));

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw ProductChanged();
                }

                await tx.CommitAsync();
                return Results.Ok();
            }
        );

        api.MapPost(
            "/products/{id:int}/restore",
            async (int id, ProductRestoreInput input, BusinessDbContext db, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Ürün sürümü gerekli.");
                EnsureRowVersion(input.RowVersion, "Ürün sürümü geçersiz.");

                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var product = await LockProductForWrite(db, id)
                    ?? throw new BusinessException("Ürün bulunamadı.", 404);
                EnsureProductVersion(product, input.RowVersion);

                if (!product.IsArchived)
                {
                    await tx.CommitAsync();
                    return Results.Ok();
                }

                product.IsArchived = false;
                product.ArchivedAt = null;
                product.ArchivedByUserId = null;
                product.ArchiveReason = "";
                db.AdminEvents.Add(AuditTrail.Event(
                    c.UserId(),
                    "ProductRestored",
                    "Product",
                    product.Id,
                    $"{product.Code} kodlu ürün yeniden satışa açıldı."));

                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw ProductChanged();
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

    private static Task<Product?> LockProductForWrite(BusinessDbContext db, int id) =>
        db.Products
            .FromSqlInterpolated($"SELECT * FROM Products WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}")
            .SingleOrDefaultAsync();

    private static BusinessException ProductChanged() =>
        new(
            "Ürün başka bir işlemde değişti. Girdilerinizi koruyarak güncel ürünü yeniden yükleyin.",
            409,
            "PRODUCT_CHANGED");

    private static void EnsureProductVersion(Product product, byte[] version)
    {
        if (!product.RowVersion.AsSpan().SequenceEqual(version))
            throw ProductChanged();
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
