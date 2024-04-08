using System.Text.Json.Serialization;

namespace BTCPayApp.Core;

public class BTCPayServerAccount(string baseUri, string email)
{
    public Uri BaseUri { get; init; } = new (WithTrailingSlash(baseUri));
    public string Email { get; init; } = email;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessExpiry { get; set; }

    [JsonConstructor]
    public BTCPayServerAccount() : this(string.Empty, string.Empty) {}

    public void SetAccess(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessExpiry = expiry;
    }

    public void ClearAccess()
    {
        AccessToken = null;
        RefreshToken = null;
        AccessExpiry = null;
    }

    private static string WithTrailingSlash(string str) =>
        str.EndsWith("/", StringComparison.InvariantCulture) ? str : str + "/";
}

