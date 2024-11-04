namespace BTCPayApp.Core.LDK;

public record APIKey(string Key, string Name, APIKeyPermission Permission)
{
    public  string ConnectionString(string user)
    {
        return $"type=app;key={Key};user={user}";
    }
}