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
    public int? DealerGroupId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class DealerGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal DiscountPercent { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
