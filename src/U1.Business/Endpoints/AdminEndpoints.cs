namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        var api = app.MapGroup("/api/admin").RequireAuthorization("Admin");
        MapDashboard(api);
        MapProducts(api);
        MapUsers(api);
        MapOrders(api);
        MapGrid(api);
        MapBanners(api);
    }
}
