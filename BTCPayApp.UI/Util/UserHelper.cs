using BTCPayServer.Client.Models;

namespace BTCPayApp.UI.Util;

public static class UserHelper
{
    public static (string, string) GetUserStatus(ApplicationUserData user)
    {
        return user switch
        {
            { Disabled: true } => ("Disabled", "danger"),
            { Approved: false, RequiresApproval: true } => ("Pending Approval", "warning"),
            { EmailConfirmed: false, RequiresEmailConfirmation: true } => ("Pending Email Verification", "warning"),
            { InvitationUrl: not null } => ("Pending Invitation", "warning"),
            _ => ("Active", "success")
        };
    }
}
