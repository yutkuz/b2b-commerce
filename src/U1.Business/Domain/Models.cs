using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

public class ProductInput
{
    [Required, StringLength(60)] public string Code { get; set; } = "";
    [Required, StringLength(180)] public string Name { get; set; } = "";
    [Required, StringLength(3000)] public string Description { get; set; } = "";
    [Required, StringLength(80)] public string Brand { get; set; } = "";
    [Required, StringLength(80)] public string ManufacturerCode { get; set; } = "";
    [StringLength(80)] public string SpecialCode1 { get; set; } = "";
    [StringLength(80)] public string SpecialCode2 { get; set; } = "";
    [StringLength(500)] public string ImageUrl { get; set; } = "/images/product.svg";
    [Range(0, int.MaxValue)] public int Stock { get; set; }
    [Range(0, 1000000)] public int CriticalStock { get; set; } = 5;
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true)] public decimal Price { get; set; }
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
}
public sealed class ProductUpdateInput : ProductInput
{
    public string? Version { get; set; }
}
public sealed class RegisterInput
{
    [Required, StringLength(80)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[+\d\s()\-]{10,25}$")] public string Phone { get; set; } = "";
    [StringLength(180)] public string Company { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string Password { get; set; } = "";
}
public sealed class UserInput
{
    [Required, StringLength(80)] public string FirstName { get; set; } = "";
    [Required, StringLength(80)] public string LastName { get; set; } = "";
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [Required, RegularExpression(@"^[+\d\s()\-]{10,25}$")] public string Phone { get; set; } = "";
    [StringLength(180)] public string Company { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public string? NewPassword { get; set; }
    public int Version { get; set; }
}
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
public record LoginInput(string Email, string Password);
public record CartInput(int ProductId, int Quantity);
public record CheckoutLine(int ProductId, int Quantity, decimal UnitPrice);
public record CheckoutInput(Guid RequestId, string? Note, CheckoutLine[]? Lines);
public record StatusInput(string Status);
public sealed class GridColumn
{
    public int Id { get; set; }
    public string Field { get; set; } = "";
    [Required, StringLength(60)] public string Label { get; set; } = "";
    public string RenderType { get; set; } = "text";
    [Range(0, 100)] public int Position { get; set; }
    [Range(60, 600)] public int Width { get; set; }
    public string Align { get; set; } = "left";
    public bool Desktop { get; set; } = true;
    public bool Tablet { get; set; } = true;
    public bool Mobile { get; set; } = true;
}
public sealed class BannerInput
{
    [Required, StringLength(100)] public string Title { get; set; } = "";
    [Required, StringLength(300)] public string Subtitle { get; set; } = "";
    [Required, StringLength(40)] public string ButtonText { get; set; } = "Ürünleri incele";
    [StringLength(100)] public string SearchTerm { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [Range(0, 100)] public int Position { get; set; }
}
public sealed class BusinessException(string message, int status = 400, string? code = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}
public static class Rules
{
    public static void Validate(object input)
    {
        if (input is null) throw new BusinessException("İstek gövdesi gerekli.");
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            throw new BusinessException("Bilgileri kontrol edin. Zorunlu alanları doldurun ve geçerli değerler girin.");
        var phone = input switch
        {
            RegisterInput register => register.Phone,
            UserInput user => user.Phone,
            _ => null
        };
        if (phone is not null && phone.Count(char.IsAsciiDigit) is < 10 or > 15)
            throw new BusinessException("Telefon numarası 10 ile 15 rakam içermeli.");
    }
}
