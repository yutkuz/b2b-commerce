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
}

public sealed class RegisterInput
{
    [Required, StringLength(80)]
    public string FirstName { get; set; } = "";

    [Required, StringLength(80)]
    public string LastName { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Required, RegularExpression(@"^[+\d\s()\-]{10,25}$")]
    public string Phone { get; set; } = "";

    [StringLength(180)]
    public string Company { get; set; } = "";

    [Required, StringLength(128, MinimumLength = 10)]
    public string Password { get; set; } = "";
}

public sealed class UserInput
{
    [Required, StringLength(80)]
    public string FirstName { get; set; } = "";

    [Required, StringLength(80)]
    public string LastName { get; set; } = "";

    [Required, EmailAddress, StringLength(200)]
    public string Email { get; set; } = "";

    [Required, RegularExpression(@"^[+\d\s()\-]{10,25}$")]
    public string Phone { get; set; } = "";

    [StringLength(180)]
    public string Company { get; set; } = "";

    public bool IsActive { get; set; } = true;
    public string? NewPassword { get; set; }
    public int Version { get; set; }
}

public record LoginInput(string Email, string Password);
public record CartInput(int ProductId, int Quantity);
public record CheckoutLine(int ProductId, int Quantity, decimal UnitPrice);
public record CheckoutInput(Guid RequestId, string? Note, CheckoutLine[]? Lines);
public record StatusInput(string Status);

public sealed class BannerInput
{
    [Required, StringLength(100)]
    public string Title { get; set; } = "";

    [Required, StringLength(300)]
    public string Subtitle { get; set; } = "";

    [Required, StringLength(40)]
    public string ButtonText { get; set; } = "Ürünleri incele";

    [StringLength(100)]
    public string SearchTerm { get; set; } = "";

    public bool IsActive { get; set; } = true;

    [Range(0, 100)]
    public int Position { get; set; }
}
