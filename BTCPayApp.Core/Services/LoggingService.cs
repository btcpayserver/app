using BTCPayApp.Core.Contracts;
using Serilog.Core;
using Serilog.Events;

namespace BTCPayApp.Core.Services;

public class LoggingService(IDataDirectoryProvider dataDirectory, LoggingLevelSwitch levelSwitch)
{
    public async Task<string> GetLatestLogAsync()
    {
        try
        {
            var logDir = Path.Combine(await dataDirectory.GetAppDataDirectory(), "logs");
            var latestLog = Directory.GetFiles(logDir)
                .OrderByDescending(f => f)
                .FirstOrDefault();

            return latestLog != null
                ? await File.ReadAllTextAsync(latestLog)
                : "No logs available";
        }
        catch (Exception ex)
        {
            return $"Error reading logs: {ex.Message}";
        }
    }

    public async Task<string> GetAppLogFilePath()
    {
        return Path.Combine(await dataDirectory.GetAppDataDirectory(), "logs");
    }

    public LogEventLevel LogLevel
    {
        get => levelSwitch.MinimumLevel;
        set => levelSwitch.MinimumLevel = value;
    }
}
