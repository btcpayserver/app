namespace BTCPayApp.Core.Models;

public class AcceptInviteResult
{
    public string? Email { get; set; }
    public bool? RequiresUserApproval { get; set; }
    public bool? EmailHasBeenConfirmed { get; set; }
    public string? PasswordSetCode { get; set; }
}
