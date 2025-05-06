using System.ComponentModel.DataAnnotations;
using BTCPayApp.UI.Util;

namespace BTCPayApp.UI.Models;

public class LoginModel
{
    public bool RequireTwoFactor { get; set; }

    [Required]
    public string? Uri { get; set; }

    [Required, EmailAddress]
    public string? Email { get; set; }

    [Required, DataType(DataType.Password)]
    public string? Password { get; set; }

    [RequiredIf(nameof(RequireTwoFactor), true)]
    public string? TwoFactorCode { get; set; }
}
