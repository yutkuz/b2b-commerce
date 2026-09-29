using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

public class ProductInput
{
    [Required, StringLength(60)]
    public string Code { get; set; } = "";

    [Required, StringLength(180)]
    public string Name { get; set; } = "";

    [Required, StringLength(3000)]
    public string Description { get; set; } = "";

    [Required, StringLength(80)]
    public string Brand { get; set; } = "";

    [Required, StringLength(80)]
    public string ManufacturerCode { get; set; } = "";

    [StringLength(80)]
    public string SpecialCode1 { get; set; } = "";

    [StringLength(80)]
    public string SpecialCode2 { get; set; } = "";

    [StringLength(500)]
    public string ImageUrl { get; set; } = "/images/product.svg";

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    [Range(0, 1000000)]
    public int CriticalStock { get; set; } = 5;

    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true)]
    public decimal Price { get; set; }

    [Range(1, int.MaxValue)]
    public int CategoryId { get; set; }
}

public sealed class ProductUpdateInput : ProductInput
{
    public string? Version { get; set; }

    [StringLength(300)]
    public string? StockReason { get; set; }
}
