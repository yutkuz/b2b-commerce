using Microsoft.EntityFrameworkCore;
using U1.Business.Data;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapDashboard(RouteGroupBuilder api)
    {
        api.MapGet(
            "/dashboard",
            async (BusinessDbContext db) =>
            {
                var lowStock = await db.Products
                    .AsNoTracking()
                    .Where(x => !x.IsArchived && x.Stock <= x.CriticalStock)
                    .OrderBy(x => x.Stock)
                    .ThenBy(x => x.Id)
                    .Take(8)
                    .Select(x => new { x.Id, x.Code, x.Name, x.Stock, x.CriticalStock })
                    .ToListAsync();

                var recentOrders = await (
                    from o in db.Orders.AsNoTracking()
                    join u in db.Users.AsNoTracking() on o.UserId equals u.Id
                    orderby o.Id descending
                    select new
                    {
                        o.Id,
                        o.Number,
                        o.CreatedAt,
                        o.Total,
                        o.Status,
                        u.Company,
                        u.FirstName,
                        u.LastName
                    })
                    .Take(8)
                    .ToListAsync();

                return new
                {
                    products = await db.Products.CountAsync(x => !x.IsArchived),
                    users = await db.Users.CountAsync(x => x.Role == "Dealer"),
                    pending = await db.Orders.CountAsync(x => x.Status == "Bekliyor"),
                    revenue = await db.Orders
                        .Where(x => x.Status == "Onaylandı" || x.Status == "Hazırlanıyor"
                            || x.Status == "Sevk edildi" || x.Status == "Teslim edildi")
                        .Select(x => (decimal?)x.Total)
                        .SumAsync() ?? 0m,
                    lowStock,
                    recentOrders,
                };
            }
        );
    }
}
