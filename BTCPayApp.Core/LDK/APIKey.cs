namespace BTCPayApp.Core.LDK;

/// <summary>
///  Represents an API key that can be used by a BTCPayServer instance to send commands to the app.
/// </summary>
/// <param name="Key">the actual key used</param>
/// <param name="Name">A name in case you need to manage multiple api keys</param>
/// <param name="Permission">Read or Write permissions, read implies being able to receive payments, write enables spending as well</param>
public record APIKey(string Key, string Name, APIKeyPermission Permission)
{
    public string ConnectionString(string user)
    {
        return $"type=app;key={Key};user={user}";
    }
}
