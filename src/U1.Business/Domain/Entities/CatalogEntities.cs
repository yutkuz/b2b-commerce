using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

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
