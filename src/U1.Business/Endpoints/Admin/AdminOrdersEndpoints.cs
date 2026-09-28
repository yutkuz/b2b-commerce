using Dapper;
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
            async (string? q, string? status, int? page, Database database) =>
            {
                using var db = database.Open();
                var args = new
                {
                    q = "%" + (q ?? "") + "%",
                    status = status ?? "",
                    offset = (Math.Clamp(page ?? 1, 1, 100000) - 1) * 20,
                };
                const string where =
                    " FROM Orders o JOIN Users u ON u.Id=o.UserId WHERE (o.Number LIKE @q OR u.Company LIKE @q OR u.FirstName LIKE @q OR u.LastName LIKE @q) AND (@status='' OR o.Status=@status)";
                return new
                {
                    items = await db.QueryAsync(
                        "SELECT o.Id,o.Number,o.CreatedAt,o.Status,o.Total,u.FirstName,u.LastName,u.Company"
                            + where
                            + " ORDER BY o.Id DESC OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",
                        args
                    ),
                    total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*)" + where, args),
                };
            }
        );
        api.MapPut(
            "/orders/{id:int}/status",
            async (int id, StatusInput input, OrderService orders) =>
            {
                if (input is null)
                    throw new BusinessException("Sipariş durumu gerekli.");
                await orders.ChangeStatus(id, input.Status);
                return Results.Ok();
            }
        );
    }
}
