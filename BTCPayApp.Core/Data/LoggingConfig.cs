using Serilog;

namespace BTCPayApp.Core.Data
{
    public static class LoggingConfig
    {
        public static void ConfigureLogging(string logFilePath = "logs/log-.txt")
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Error() // Set the minimum logging level
                .WriteTo.Console()    // Write to the console (optional)
                .WriteTo.File(
                    logFilePath,         // Path to the log files
                    rollingInterval: RollingInterval.Day, // Creates a new log file daily
                    retainedFileCountLimit: 7,           // Retain logs for 7 days
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Message}{NewLine}{Exception}" // Format
                )
                .CreateLogger();
        }
    }
}
