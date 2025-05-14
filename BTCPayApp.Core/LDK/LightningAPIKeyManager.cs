using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.LDK;

public class LightningAPIKeyManager(ConfigProvider configProvider)
{
    private const string LightningAPIKeyConfigKey = "LightningAPIKeys";

    public async Task<APIKey> GetKeyForStore(string storeId, APIKeyPermission permission)
    {
        return await GetOrCreate($"BTCPay Store {storeId}", permission);
    }

    public async Task Revoke(string key)
    {
        var keys = await List();
        if (keys.RemoveAll(k => k.Key == key)>0)
            await configProvider.Set(LightningAPIKeyConfigKey, keys, true);
    }

    public  async Task<bool> CheckPermission(string key, APIKeyPermission permission)
    {
        var keys = await List();
        return keys.Any(k => k.Key == key && k.Permission >= permission);
    }

    private async Task<List<APIKey>> List()
    {
        var keys = await configProvider.Get<List<APIKey>>(LightningAPIKeyConfigKey) ?? [];
        return keys;
    }

    private async Task<APIKey?> Get(string name, APIKeyPermission permission)
    {
        var keys = await List();
        return keys.FirstOrDefault(k => k.Name == name && k.Permission == permission);
    }

    private async Task<APIKey> Create(string name, APIKeyPermission permission)
    {
        var keys = await List();
        var newKey = new APIKey(Guid.NewGuid().ToString(), name, permission);
        keys.Add(newKey);
        await configProvider.Set(LightningAPIKeyConfigKey, keys, true);
        return newKey;
    }

    private async Task<APIKey> GetOrCreate(string name, APIKeyPermission permission)
    {
        return await Get(name, permission) ?? await Create(name, permission);
    }
}
