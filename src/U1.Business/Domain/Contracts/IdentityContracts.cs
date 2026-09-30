using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

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
    public int? DealerGroupId { get; set; }
    public string? NewPassword { get; set; }
    public int Version { get; set; }
}

public sealed class ProfileUpdateInput
{
    [Required, StringLength(80)]
    public string FirstName { get; set; } = "";

    [Required, StringLength(80)]
    public string LastName { get; set; } = "";

    [Required, RegularExpression(@"^[+\d\s()\-]{10,25}$")]
    public string Phone { get; set; } = "";

    [StringLength(180)]
    public string Company { get; set; } = "";

    public byte[] RowVersion { get; set; } = [];
}

public record LoginInput(string Email, string Password);
