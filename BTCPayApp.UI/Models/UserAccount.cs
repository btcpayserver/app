using System.Security.Claims;

namespace BTCPayApp.UI.Models;

public class UserAccount
{
    public string Id { get; set; }
    public string Email { get; set; }
    public string Token { get; set; }
    public ClaimsPrincipal Principal { get; set; } = new();
}
