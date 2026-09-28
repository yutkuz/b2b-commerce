using Dapper;
using U1.Business.Data;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapDashboard(RouteGroupBuilder api)
    {
        api.MapGet(
            "/dashboard",
            async (Database database) =>
            {
                using var db = database.Open();
                return new
                {
                    products = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products"),
                    users = await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM Users WHERE Role='Dealer'"
                    ),
                    pending = await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM Orders WHERE Status=N'Bekliyor'"
                    ),
                    revenue = await db.ExecuteScalarAsync<decimal>(
                        "SELECT COALESCE(SUM(Total),0) FROM Orders WHERE Status=N'Onaylandı'"
                    ),
                    lowStock = await db.QueryAsync(
                        "SELECT TOP(8) Id,Code,Name,Stock,CriticalStock FROM Products WHERE Stock<=CriticalStock ORDER BY Stock,Id"
                    ),
                    recentOrders = await db.QueryAsync(
                        "SELECT TOP(8) o.Id,o.Number,o.CreatedAt,o.Total,o.Status,u.Company,u.FirstName,u.LastName FROM Orders o JOIN Users u ON u.Id=o.UserId ORDER BY o.Id DESC"
                    ),
                };
            }
        );
    }
}
