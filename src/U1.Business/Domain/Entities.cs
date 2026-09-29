using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

public sealed class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Company { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Dealer";
    public bool IsActive { get; set; }
    public int AuthVersion { get; set; }
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ManufacturerCode { get; set; } = "";
    public string SpecialCode1 { get; set; } = "";
    public string SpecialCode2 { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public int Stock { get; set; }
    public int CriticalStock { get; set; }
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

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

public sealed class GridColumn
{
    public int Id { get; set; }
    public string Field { get; set; } = "";

    [Required, StringLength(60)]
    public string Label { get; set; } = "";

    public string RenderType { get; set; } = "text";

    [Range(0, 100)]
    public int Position { get; set; }

    [Range(60, 600)]
    public int Width { get; set; }

    public string Align { get; set; } = "left";
    public bool Desktop { get; set; } = true;
    public bool Tablet { get; set; } = true;
    public bool Mobile { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class Banner
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string ButtonText { get; set; } = "";
    public string SearchTerm { get; set; } = "";
    public bool IsActive { get; set; }
    public int Position { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SchemaVersion
{
    public int Version { get; set; }
}

public sealed class DemoSetup
{
    public string Component { get; set; } = "";
    public string Status { get; set; } = "";
}
