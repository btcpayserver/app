using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.Helpers;

public class LogHelper(IDataDirectoryProvider storageService)
{
    public async Task<string> GetLogPath()
    {
        return Path.Combine(await storageService.GetAppDataDirectory(), "logs", "btcpayapp-.log");
    }
}
