using System.Text.Json;
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class XamarinEssentialsConfigProvider : IConfigProvider
{
    public async Task<T?> Get<T>(string key)
    {
        var result = Preferences.Get(key, null);
        return result is null ? default : JsonSerializer.Deserialize<T?>(result);
    }

    public Task Set<T>(string key, T? value)
    {
        if (value is null)
        {
            Preferences.Remove(key);
        }
        else
        {
            Preferences.Set(key, JsonSerializer.Serialize(value));
        }

        return Task.CompletedTask;
    }
}