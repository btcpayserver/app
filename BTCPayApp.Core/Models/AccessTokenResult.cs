namespace BTCPayApp.Core.Models;

public class AccessTokenResult
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset Expiry { get; set; }
}
