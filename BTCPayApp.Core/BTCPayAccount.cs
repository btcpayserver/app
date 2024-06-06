namespace BTCPayApp.Core;

public class BTCPayAccount(string baseUri, string email)
{
    public string Id = $"{new Uri(baseUri).Host}:{email}";
    public string BaseUri { get; private set; } = baseUri;
    public string Email { get; private set; } = email;
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessExpiry { get; set; }
    public string? CurrentStoreId { get; set; }


    public void SetAccess(string accessToken, string refreshToken, long expiresInSeconds, DateTimeOffset? expiryOffset = null)
    {
        var expiry = (expiryOffset ?? DateTimeOffset.Now) + TimeSpan.FromSeconds(expiresInSeconds);
        SetAccess(accessToken, refreshToken, expiry);
    }

    public void SetAccess(string accessToken, string refreshToken, DateTimeOffset expiry)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        AccessExpiry = expiry;
    }

    public void ClearAccess()
    {
        AccessToken = RefreshToken = null;
        AccessExpiry = null;
    }

    public bool HasTokens => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);
}

