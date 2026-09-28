using Dapper;
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
            async (Database database) =>
            {
                using var db = database.Open();
                return new
                {
                    categories = await db.QueryAsync(
                        "SELECT c.Id,c.Name,COUNT(p.Id) AS ProductCount FROM Categories c LEFT JOIN Products p ON p.CategoryId=c.Id GROUP BY c.Id,c.Name ORDER BY c.Id"
                    ),
                    brands = await db.QueryAsync<string>(
                        "SELECT DISTINCT Brand FROM Products ORDER BY Brand"
                    ),
                    columns = await db.QueryAsync<GridColumn>(
                        "SELECT * FROM GridColumns ORDER BY Position,Id"
                    ),
                    banners = await db.QueryAsync(
                        "SELECT * FROM Banners WHERE IsActive=1 ORDER BY Position,Id"
                    ),
                };
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
                Database database
            ) =>
            {
                using var db = database.Open();
                var pageNumber = Math.Clamp(page ?? 1, 1, 100000);
                var order = sort switch
                {
                    "price-asc" => "p.Price ASC,p.Id",
                    "price-desc" => "p.Price DESC,p.Id",
                    "name" => "p.Name,p.Id",
                    "newest" => "p.CreatedAt DESC,p.Id DESC",
                    _ => "p.Id",
                };
                const string where = """
                    FROM Products p JOIN Categories c ON c.Id=p.CategoryId WHERE
                    (@q='' OR p.Name LIKE @search ESCAPE '~' OR p.Code LIKE @search ESCAPE '~' OR p.Description LIKE @search ESCAPE '~'
                     OR p.Brand LIKE @search ESCAPE '~' OR p.ManufacturerCode LIKE @search ESCAPE '~' OR p.SpecialCode1 LIKE @search ESCAPE '~'
                     OR p.SpecialCode2 LIKE @search ESCAPE '~' OR p.ImageUrl LIKE @search ESCAPE '~' OR c.Name LIKE @search ESCAPE '~')
                    AND (@category IS NULL OR p.CategoryId=@category) AND (@brand='' OR p.Brand=@brand)
                    AND (@stock='' OR (@stock='available' AND p.Stock>0) OR (@stock='critical' AND p.Stock>0 AND p.Stock<=p.CriticalStock) OR (@stock='empty' AND p.Stock=0))
                    """;
                q = (q ?? "").Trim();
                if (q.Length > 200)
                    throw new BusinessException("Arama metni en fazla 200 karakter olabilir.");
                var args = new
                {
                    q,
                    search = "%"
                        + q.Replace("~", "~~")
                            .Replace("%", "~%")
                            .Replace("_", "~_")
                            .Replace("[", "~[")
                        + "%",
                    category,
                    brand = brand ?? "",
                    stock = stock ?? "",
                    offset = (pageNumber - 1) * 20,
                };
                var total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) " + where, args);
                var items = await db.QueryAsync(
                    "SELECT p.*,c.Name AS Category "
                        + where
                        + $" ORDER BY {order} OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",
                    args
                );
                return new
                {
                    items,
                    total,
                    page = pageNumber,
                    pageSize = 20,
                };
            }
        );
        api.MapGet(
            "/products/{id:int}",
            async (int id, Database database) =>
            {
                using var db = database.Open();
                return await db.QuerySingleOrDefaultAsync(
                        "SELECT p.*,c.Name AS Category FROM Products p JOIN Categories c ON c.Id=p.CategoryId WHERE p.Id=@id",
                        new { id }
                    ) ?? throw new BusinessException("Ürün bulunamadı.", 404);
            }
        );
    }
}
