using U1.Business.Domain;

namespace U1.Business.Services;

public static class AuditTrail
{
    public static AdminEvent Event(
        int actorUserId,
        string eventType,
        string entityType,
        int? entityId,
        string summary) => new()
        {
            ActorUserId = actorUserId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary
        };

    public static StockMovement Stock(
        int productId,
        int previousStock,
        int newStock,
        string movementType,
        string reason,
        int? actorUserId = null,
        int? orderId = null,
        long? adminEventId = null) => new()
        {
            ProductId = productId,
            OrderId = orderId,
            ActorUserId = actorUserId,
            AdminEventId = adminEventId,
            MovementType = movementType,
            QuantityDelta = newStock - previousStock,
            PreviousStock = previousStock,
            NewStock = newStock,
            Reason = reason
        };
}
