namespace BTCPayApp.Core;

public class BTCPayAccount(string baseUri, string email, string? accessToken = null)
{
    public const string Key = "account";
    public string Id { get; private set; } = $"{new Uri(baseUri).Host}:{email}";
    public string BaseUri { get; private set; } = WithTrailingSlash(baseUri);
    public string Email { get; private set; } = email;
    public string? AccessToken { get; set; } = accessToken;

    private static string WithTrailingSlash(string s)
    {
        return s.EndsWith('/') ? s : $"{s}/";
    }
}

