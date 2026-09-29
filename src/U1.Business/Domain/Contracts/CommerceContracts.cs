namespace U1.Business.Domain;

public record CartInput(int ProductId, int Quantity);
public record CheckoutLine(int ProductId, int Quantity, decimal UnitPrice);
public record CheckoutInput(Guid RequestId, string? Note, CheckoutLine[]? Lines);
public record StatusInput(string Status);
