using Serilog;
using Serilog.Core;

namespace BTCPayApp.Core.Data;

public static class LoggingConfig
{
    public static void ConfigureLogging(LoggingLevelSwitch levelSwitch, string logFilePath = "logs/btcpayapp-.log")
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console()    // Write to the console (optional)
            .WriteTo.File(
                logFilePath,         // Path to the log files
                rollingInterval: RollingInterval.Day, // Creates a new log file daily
                retainedFileCountLimit: 7,           // Retain logs for 7 days
                levelSwitch: levelSwitch, // Use the provided level switch
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}" // Format
            )
            .CreateLogger();
    }
}
