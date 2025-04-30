using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace BTCPayApp.Core.Data;

public static class LoggingConfig
{
    public static void ConfigureLogging(IServiceCollection serviceCollection)
    {
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var configProvider = serviceProvider.GetRequiredService<ConfigProvider>();
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var defaultMinLevel = env == "Development" ? LogEventLevel.Debug : LogEventLevel.Information;
        var logLevel = configProvider.Get<LogEventLevel?>("logLevel").ConfigureAwait(false).GetAwaiter().GetResult();
        var levelSwitch = new LoggingLevelSwitch { MinimumLevel = logLevel ?? defaultMinLevel };
        serviceCollection.AddSingleton(levelSwitch);
        serviceCollection.AddSerilog();

        var outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} ({SourceContext}){NewLine}{Exception}";
        var config = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: outputTemplate) // Write to the console (optional)
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        /*
        "LDK": "Trace",
        "LDK.lightning::ln::peer_handler": "Debug",
        "LDK.lightning::routing::gossip": "Information",
        "LDK.BTCPayApp.Core.LDK.LDKPeerHandler": "Information",
        "LDK.lightning_background_processor": "Information"*/

        if (env == "Production")
        {
            var dirProvider = serviceProvider.GetRequiredService<IDataDirectoryProvider>();
            var appDir = dirProvider.GetAppDataDirectory().ConfigureAwait(false).GetAwaiter().GetResult();
            var logFilePath = Path.Combine(appDir, "logs", "btcpayapp-.log");
            config.WriteTo.File(
                logFilePath,
                retainedFileCountLimit: 7,
                rollingInterval: RollingInterval.Day,
                outputTemplate: outputTemplate
            );
        }

        Log.Logger = config.CreateLogger();
    }
}
