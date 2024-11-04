using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.LDK;

public class LightningAPIKeyManager
{
    private const string LightningAPIKeyConfigKey = "LightningAPIKeys";
    private readonly ConfigProvider _configProvider;

    
    public LightningAPIKeyManager(ConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }
    
    public async Task<List<APIKey>> List()
    {
        var keys = await _configProvider.Get<List<APIKey>>(LightningAPIKeyConfigKey) ?? [];
        return keys;
    }
    public async Task Revoke(string key)
    {
        var keys = await List();
        if(keys.RemoveAll(k => k.Key == key)>0)
            await _configProvider.Set(LightningAPIKeyConfigKey, keys, true);
    }

    public async Task<APIKey> Create(string name, APIKeyPermission permission)
    {
        var keys = await List();
        var newKey = new APIKey(Guid.NewGuid().ToString(), name, permission);
        keys.Add(newKey);
        await _configProvider.Set(LightningAPIKeyConfigKey, keys, true);
        return newKey;

    }
    public  async Task<bool> CheckPermission(string key, APIKeyPermission permission)
    {
        var keys = await List();
        return keys.Any(k => k.Key == key && k.Permission >= permission);
    }

}