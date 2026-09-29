using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapOrders(RouteGroupBuilder api)
    {
        api.MapGet(
            "/orders",
            async (string? q, string? status, int? page, BusinessDbContext db) =>
            {
                var search = q ?? "";
                var statusFilter = status ?? "";
                var offset = (Math.Clamp(page ?? 1, 1, 100000) - 1) * 20;

                var query =
                    from o in db.Orders.AsNoTracking()
                    join u in db.Users.AsNoTracking() on o.UserId equals u.Id
                    where (
                        o.Number.Contains(search)
                        || u.Company.Contains(search)
                        || u.FirstName.Contains(search)
                        || u.LastName.Contains(search))
                        && (statusFilter == "" || o.Status == statusFilter)
                    select new
                    {
                        o.Id,
                        o.Number,
                        o.CreatedAt,
                        o.Status,
                        o.Total,
                        u.FirstName,
                        u.LastName,
                        u.Company
                    };

                return new
                {
                    items = await query
                        .OrderByDescending(x => x.Id)
                        .Skip(offset)
                        .Take(20)
                        .ToListAsync(),
                    total = await query.CountAsync(),
                };
            }
        );

        api.MapPut(
            "/orders/{id:int}/status",
            async (int id, StatusInput input, OrderService orders, HttpContext c) =>
            {
                if (input is null)
                    throw new BusinessException("Sipariş durumu gerekli.");
                await orders.ChangeStatus(id, input.Status, c.UserId());
                return Results.Ok();
            }
        );
    }
}
