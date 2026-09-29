namespace U1.Business.Domain;

public sealed class AdminEvent
{
    public long Id { get; set; }
    public int ActorUserId { get; set; }
    public string EventType { get; set; } = "";
    public string EntityType { get; set; } = "";
    public int? EntityId { get; set; }
    public string Summary { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public int? OrderId { get; set; }
    public int? ActorUserId { get; set; }
    public long? AdminEventId { get; set; }
    public string MovementType { get; set; } = "";
    public int QuantityDelta { get; set; }
    public int PreviousStock { get; set; }
    public int NewStock { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
