using System.ComponentModel.DataAnnotations;

namespace U1.Business.Domain;

public sealed class BusinessException(string message, int status = 400, string? code = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

public static class Rules
{
    public static void Validate(object input)
    {
        if (input is null)
            throw new BusinessException("İstek gövdesi gerekli.");

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
        {
            throw new BusinessException(
                "Bilgileri kontrol edin. Zorunlu alanları doldurun ve geçerli değerler girin.");
        }

        var phone = input switch
        {
            RegisterInput register => register.Phone,
            UserInput user => user.Phone,
            ProfileUpdateInput profile => profile.Phone,
            _ => null
        };

        if (phone is not null && phone.Count(char.IsAsciiDigit) is < 10 or > 15)
            throw new BusinessException("Telefon numarası 10 ile 15 rakam içermeli.");
    }
}
