namespace BTCPayApp.Core.Models;

public class LoginInfoResult
{
    public string? Email { get; set; }
    public bool? HasPassword { get; set; }
    public bool? IsEmailConfirmed { get; set; }
    public bool? IsApproved { get; set; }
    public bool? IsDisabled { get; set; }
}
