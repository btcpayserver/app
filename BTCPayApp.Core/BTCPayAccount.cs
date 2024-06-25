using Newtonsoft.Json;

namespace BTCPayApp.Core;

public class BTCPayAccount(string baseUri, string email)
{
    public static string GetId(string baseUri, string email) => $"{new Uri(baseUri).Host}:{email}";
    public readonly string Id = GetId(baseUri, email);
    public string BaseUri { get; private set; } = WithTrailingSlash(baseUri);
    public string Email { get; private set; } = email;
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }
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

    public void SetInfo(string email, string? name, string? imageUrl)
    {
        Email = email;
        Name = name;
        ImageUrl = imageUrl;
    }

    [JsonIgnore]
    public bool HasTokens => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(RefreshToken);

    private static string WithTrailingSlash(string s)
    {
        return s.EndsWith('/') ? s : $"{s}/";
    }
}

