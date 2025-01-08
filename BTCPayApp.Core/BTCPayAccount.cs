namespace BTCPayApp.Core;

public class BTCPayAccount(string baseUri, string email)
{
    public static string GetId(string baseUri, string email) => $"{new Uri(baseUri).Host}:{email}";
    public readonly string Id = GetId(baseUri, email);
    public string BaseUri { get; private set; } = WithTrailingSlash(baseUri);
    public string Email { get; private set; } = email;
    public string? AccessToken { get; set; }
    public string? Name { get; set; }
    public string? ImageUrl { get; set; }

    // TODO: Store this separately
    public string? CurrentStoreId { get; set; }

    public void SetInfo(string email, string? name, string? imageUrl)
    {
        Email = email;
        Name = name;
        ImageUrl = imageUrl;
    }

    private static string WithTrailingSlash(string s)
    {
        return s.EndsWith('/') ? s : $"{s}/";
    }
}

