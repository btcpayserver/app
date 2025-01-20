using BTCPayApp.Core.Contracts;
using NBitcoin;

namespace BTCPayApp.Core.Helpers;

public static class ConfigExtensions
{
    private const string ConfigDeviceIdentifierKey = "deviceIdentifier";
    public static async Task<long> GetDeviceIdentifier(this ConfigProvider configProvider)
    {
        return await configProvider.GetOrSet(ConfigDeviceIdentifierKey, () => Task.FromResult(RandomUtils.GetInt64()), false);
    }
}
