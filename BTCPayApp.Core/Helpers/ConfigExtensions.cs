using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using NBitcoin;

namespace BTCPayApp.Core.Helpers;

public static class ConfigExtensions
{
    
    private const string ConfigDeviceIdentifierKey = "deviceIdentifier";
    public static async Task<long> GetDeviceIdentifier(this IConfigProvider configProvider)
    {
        return await configProvider.GetOrSet(ConfigDeviceIdentifierKey,
            async () => RandomUtils.GetInt64(), false);
    }
}