using Microsoft.EntityFrameworkCore;
using U1.Business.Data;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapHistory(RouteGroupBuilder api)
    {
        api.MapGet(
            "/history",
            async (
                DateTime? from,
                DateTime? to,
                int? productId,
                int? userId,
                string? action,
                int? page,
                BusinessDbContext db) =>
            {
                var pageNumber = Math.Clamp(page ?? 1, 1, 100000);
                var stock = db.StockMovements.AsNoTracking().AsQueryable();
                var events = db.AdminEvents.AsNoTracking().AsQueryable();

                if (from is not null)
                {
                    stock = stock.Where(x => x.CreatedAt >= from);
                    events = events.Where(x => x.CreatedAt >= from);
                }
                if (to is not null)
                {
                    stock = stock.Where(x => x.CreatedAt <= to);
                    events = events.Where(x => x.CreatedAt <= to);
                }
                if (productId is not null)
                {
                    stock = stock.Where(x => x.ProductId == productId);
                    events = events.Where(x => x.EntityType == "Product" && x.EntityId == productId);
                }
                if (userId is not null)
                {
                    stock = stock.Where(x => x.ActorUserId == userId);
                    events = events.Where(x =>
                        x.ActorUserId == userId
                        || x.EntityType == "User" && x.EntityId == userId);
                }
                if (!string.IsNullOrWhiteSpace(action))
                {
                    stock = stock.Where(x => x.MovementType == action);
                    events = events.Where(x => x.EventType == action);
                }

                var stockRows =
                    from movement in stock
                    join product in db.Products.AsNoTracking() on movement.ProductId equals product.Id
                    join actor in db.Users.AsNoTracking() on movement.ActorUserId equals actor.Id into actors
                    from actor in actors.DefaultIfEmpty()
                    select new HistoryRow
                    {
                        Key = "S-" + movement.Id,
                        Kind = "Stock",
                        CreatedAt = movement.CreatedAt,
                        Action = movement.MovementType,
                        ActorUserId = movement.ActorUserId,
                        Actor = actor == null ? "Sistem" : actor.FirstName + " " + actor.LastName,
                        ProductId = movement.ProductId,
                        Product = product.Code + " · " + product.Name,
                        EntityType = "Product",
                        EntityId = movement.ProductId,
                        OrderId = movement.OrderId,
                        QuantityDelta = movement.QuantityDelta,
                        PreviousStock = movement.PreviousStock,
                        NewStock = movement.NewStock,
                        Summary = movement.Reason
                    };

                var eventRows =
                    from audit in events
                    join actor in db.Users.AsNoTracking() on audit.ActorUserId equals actor.Id
                    select new HistoryRow
                    {
                        Key = "A-" + audit.Id,
                        Kind = "Admin",
                        CreatedAt = audit.CreatedAt,
                        Action = audit.EventType,
                        ActorUserId = audit.ActorUserId,
                        Actor = actor.FirstName + " " + actor.LastName,
                        ProductId = audit.EntityType == "Product" ? audit.EntityId : null,
                        Product = null,
                        EntityType = audit.EntityType,
                        EntityId = audit.EntityId,
                        OrderId = audit.EntityType == "Order" ? audit.EntityId : null,
                        QuantityDelta = null,
                        PreviousStock = null,
                        NewStock = null,
                        Summary = audit.Summary
                    };

                var query = stockRows.Concat(eventRows);
                var total = await query.CountAsync();
                var items = await query
                    .OrderByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Key)
                    .Skip((pageNumber - 1) * 20)
                    .Take(20)
                    .ToListAsync();

                return new { items, total, page = pageNumber, pageSize = 20 };
            }
        );
    }

    private sealed class HistoryRow
    {
        public string Key { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Action { get; set; } = "";
        public int? ActorUserId { get; set; }
        public string Actor { get; set; } = "";
        public int? ProductId { get; set; }
        public string? Product { get; set; }
        public string EntityType { get; set; } = "";
        public int? EntityId { get; set; }
        public int? OrderId { get; set; }
        public int? QuantityDelta { get; set; }
        public int? PreviousStock { get; set; }
        public int? NewStock { get; set; }
        public string Summary { get; set; } = "";
    }
}
