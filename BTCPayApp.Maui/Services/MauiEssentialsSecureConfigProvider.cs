using System.Text.Json;
using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Maui.Services;

public class MauiEssentialsSecureConfigProvider : ISecureConfigProvider
{
    private static string IndexKey => "SecureConfigProviderIndex";

    public async Task<T?> Get<T>(string key)
    {
        var raw = await SecureStorage.GetAsync(key);
        return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw);
    }

    public async Task Set<T>(string key, T? value)
    {
        var index = await GetIndex();
        if (value is null)
        {
            SecureStorage.Remove(key);
            if (index.Contains(key))
            {
                index = index.Where(k => k != key).ToArray();
                await SetIndex(index);
            }
        }
        else
        {
            await SecureStorage.SetAsync(key, JsonSerializer.Serialize(value));
            if (!index.Contains(key))
            {
                index = index.Append(key).ToArray();
                await SetIndex(index);
            }
        }
    }

    private static async Task<string[]> GetIndex()
    {
        var index = await SecureStorage.GetAsync(IndexKey);
        return string.IsNullOrEmpty(index) ? [] : JsonSerializer.Deserialize<string[]>(index)!;
    }

    private static async Task SetIndex(string[] index)
    {
        await SecureStorage.SetAsync(IndexKey, JsonSerializer.Serialize(index));
    }

    public static async Task<IEnumerable<string>> List(string prefix)
    {
        var index = await GetIndex();
        return index.Where(key => key.StartsWith(prefix));
    }
}
