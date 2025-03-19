using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.Helpers
{
    public class LogHelper
    {
        private readonly IDataDirectoryProvider _storageService;

        public LogHelper(IDataDirectoryProvider storageService)
        {
            _storageService = storageService;
        }

        public async Task<string> GetLogPath()
        {
            return Path.Combine(await _storageService.GetAppDataDirectory(), "logs", "app_log.txt");
        }
    }
}
