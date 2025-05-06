using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace BTCPayApp.Core.Data;

public static class LoggingConfig
{
    private const string OutputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} ({SourceContext}){NewLine}{Exception}";

    public static void ConfigureLogging(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSerilog();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dirProvider = serviceProvider.GetRequiredService<IDataDirectoryProvider>();
        var appDir = dirProvider.GetAppDataDirectory().ConfigureAwait(false).GetAwaiter().GetResult();
        var isDevEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        var minLogLevel = isDevEnv ? LogEventLevel.Verbose : LogEventLevel.Information;
        var config = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.SQLite($"{appDir}/app.db")
            .WriteTo.Console(outputTemplate: OutputTemplate, restrictedToMinimumLevel: minLogLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning);

        /*
        "LDK": "Trace",
        "LDK.lightning::ln::peer_handler": "Debug",
        "LDK.lightning::routing::gossip": "Information",
        "LDK.BTCPayApp.Core.LDK.LDKPeerHandler": "Information",
        "LDK.lightning_background_processor": "Information"*/

        Log.Logger = config.CreateLogger();
    }
}
