namespace U1.Business.Domain;

public sealed class Cart
{
    public int Id { get; set; }
    public int UserId { get; set; }
}

public sealed class CartItem
{
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Bekliyor";
    public decimal Total { get; set; }
    public Guid RequestId { get; set; }
    public string Note { get; set; } = "";
    public string AdminNote { get; set; } = "";
    public string RejectionReason { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

public sealed class OrderStatusHistory
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public int? ActorUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime ChangedAt { get; set; }
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
