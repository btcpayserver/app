using BTCPayApp.Core.Contracts;
using NBitcoin;

namespace BTCPayApp.Core.Helpers;

public static class ConfigExtensions
{
    private const string ConfigDeviceIdentifierKey = "deviceIdentifier";

    /*
    public static async Task<long> GetDeviceIdentifier(this ISecureConfigProvider configProvider)
    {
        var id = await configProvider.Get<long>(ConfigDeviceIdentifierKey);
        if (id == 0)
        {
            id = RandomUtils.GetInt64();
            await configProvider.Set(ConfigDeviceIdentifierKey, id);
        }
        return id;
    }
    */

    public static async Task<long> GetDeviceIdentifier(this ConfigProvider configProvider)
    {
        return await configProvider.GetOrSet(ConfigDeviceIdentifierKey, () => Task.FromResult(RandomUtils.GetInt64()), false);
    }
}
