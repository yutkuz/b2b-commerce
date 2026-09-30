using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalog(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet(
            "/catalog/meta",
            async (BusinessDbContext db) =>
            {
                var categories = await db.Categories
                    .AsNoTracking()
                    .OrderBy(c => c.Id)
                    .Select(c => new
                    {
                        c.Id,
                        c.Name,
                        ProductCount = db.Products.Count(p => p.CategoryId == c.Id && !p.IsArchived)
                    })
                    .ToListAsync();

                var brands = await db.Products
                    .AsNoTracking()
                    .Where(p => !p.IsArchived)
                    .Select(p => p.Brand)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

                var columns = await db.GridColumns
                    .AsNoTracking()
                    .OrderBy(x => x.Position)
                    .ThenBy(x => x.Id)
                    .ToListAsync();

                var banners = await db.Banners
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Position)
                    .ThenBy(x => x.Id)
                    .ToListAsync();

                return new { categories, brands, columns, banners };
            }
        );

        api.MapGet(
            "/products",
            async (
                string? q,
                int? category,
                string? brand,
                string? stock,
                string? sort,
                int? page,
                BusinessDbContext db
            ) =>
            {
                var pageNumber = Math.Clamp(page ?? 1, 1, 100000);
                q = (q ?? "").Trim();
                if (q.Length > 200)
                    throw new BusinessException("Arama metni en fazla 200 karakter olabilir.");

                var query =
                    from p in db.Products.AsNoTracking()
                    join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
                    where !p.IsArchived
                    select new { Product = p, Category = c.Name };

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
                        || EF.Functions.Like(x.Product.Description, search, "~")
                        || EF.Functions.Like(x.Product.Brand, search, "~")
                        || EF.Functions.Like(x.Product.ManufacturerCode, search, "~")
                        || EF.Functions.Like(x.Product.SpecialCode1, search, "~")
                        || EF.Functions.Like(x.Product.SpecialCode2, search, "~")
                        || EF.Functions.Like(x.Product.ImageUrl, search, "~")
                        || EF.Functions.Like(x.Category, search, "~"));
                }

                if (category is not null)
                    query = query.Where(x => x.Product.CategoryId == category);
                if (!string.IsNullOrEmpty(brand))
                    query = query.Where(x => x.Product.Brand == brand);

                query = (stock ?? "") switch
                {
                    "available" => query.Where(x => x.Product.Stock > 0),
                    "critical" => query.Where(x => x.Product.Stock > 0 && x.Product.Stock <= x.Product.CriticalStock),
                    "empty" => query.Where(x => x.Product.Stock == 0),
                    _ => query
                };

                var total = await query.CountAsync();
                var ordered = sort switch
                {
                    "price-asc" => query.OrderBy(x => x.Product.Price).ThenBy(x => x.Product.Id),
                    "price-desc" => query.OrderByDescending(x => x.Product.Price).ThenBy(x => x.Product.Id),
                    "name" => query.OrderBy(x => x.Product.Name).ThenBy(x => x.Product.Id),
                    "newest" => query.OrderByDescending(x => x.Product.CreatedAt).ThenByDescending(x => x.Product.Id),
                    _ => query.OrderBy(x => x.Product.Id)
                };

                var items = await ordered
                    .Skip((pageNumber - 1) * 20)
                    .Take(20)
                    .Select(x => new
                    {
                        x.Product.Id,
                        x.Product.Code,
                        x.Product.Name,
                        x.Product.Description,
                        x.Product.Brand,
                        x.Product.ManufacturerCode,
                        x.Product.SpecialCode1,
                        x.Product.SpecialCode2,
                        x.Product.ImageUrl,
                        x.Product.Stock,
                        x.Product.CriticalStock,
                        x.Product.Price,
                        x.Product.CategoryId,
                        x.Product.CreatedAt,
                        x.Product.RowVersion,
                        x.Category
                    })
                    .ToListAsync();

                return new
                {
                    items,
                    total,
                    page = pageNumber,
                    pageSize = 20,
                };
            }
        ).RequireRateLimiting("catalog-search");

        api.MapGet(
            "/products/{id:int}",
            async (int id, BusinessDbContext db) =>
            {
                var product = await (
                    from p in db.Products.AsNoTracking()
                    join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
                    where p.Id == id && !p.IsArchived
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
                        p.RowVersion,
                        Category = c.Name
                    }).SingleOrDefaultAsync();

                return product ?? throw new BusinessException("Ürün bulunamadı.", 404);
            }
        );
    }
}
