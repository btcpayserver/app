using BTCPayApp.Core.Contracts;

namespace BTCPayApp.Core.Services
{
    public class LogReaderService
    {
        private readonly IDataDirectoryProvider dataDirectory;

        public LogReaderService(IDataDirectoryProvider dataDirectory)
        {
            this.dataDirectory = dataDirectory;
        }
        public async Task<string> GetLatestLogAsync()
        {
            try
            {
                string logDir = Path.Combine(await dataDirectory.GetAppDataDirectory(), "logs");
                var latestLog = Directory.GetFiles(logDir)
                    .OrderByDescending(f => f)
                    .FirstOrDefault();

                if (latestLog != null)
                {
                    return await File.ReadAllTextAsync(latestLog);
                }
                return "No logs available";
            }
            catch (Exception ex)
            {
                return $"Error reading logs: {ex.Message}";
            }
        }
    }
}
