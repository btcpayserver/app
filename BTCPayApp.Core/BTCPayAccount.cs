namespace BTCPayApp.Core;

public class BTCPayAccount(string baseUri, string email, string? ownerToken = null)
{
    public const string Key = "account";
    public string Id { get; private set; } = $"{new Uri(baseUri).Host}:{email}";
    public string BaseUri { get; private set; } = WithTrailingSlash(baseUri);
    public string Email { get; private set; } = email;
    public string? OwnerToken { get; set; } = ownerToken;
    public string? UserToken { get; set; }

    private static string WithTrailingSlash(string s)
    {
        return s.EndsWith('/') ? s : $"{s}/";
    }
}

