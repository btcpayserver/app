using System.Drawing;
using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Extensions;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Desktop;
using BTCPayApp.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Photino.Blazor;
using Photino.NET;

namespace BTCPayApp.Photino;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        

        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        builder.Services.TryAddSingleton<IConfiguration>(_ =>
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddEnvironmentVariables();
            return configBuilder.Build();
        });
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.ConfigureBTCPayAppDesktop();
        builder.Services.AddLogging();
#if DEBUG
        builder.Services.AddDangerousSSLSettingsForDev();
#endif
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");
        var serviceProvider = builder.Services.BuildServiceProvider();
        var dirProvider = serviceProvider.GetRequiredService<IDataDirectoryProvider>();

        var logHelper = new LogHelper(dirProvider);
        string logFilePath = logHelper.GetLogPath().GetAwaiter().GetResult();
        // Configure logging
        LoggingConfig.ConfigureLogging(logFilePath);

        var app = builder.Build();

        // customize window.
        app.MainWindow
            .SetResizable(true)
            .SetZoom(0)
            .SetTitle("BTCPay Server");

        app.MainWindow.Center();
        Size? size = null;
        Size? lastAcceptedSize = null;

        app.MainWindow.WindowSizeChangedHandler += (sender, e) =>
        {
            try
            {
                var window = sender as PhotinoWindow;

                if (size == null)
                {
                    size = e;
                }
                else
                {
                    var zoomx = (double)e.Width / ((Size)size).Width * 100;
                    var zoomy = (double)e.Height / ((Size)size).Height * 100;
                    var zoom = Math.Min(zoomx, zoomy);
                    if (zoom < 75 && lastAcceptedSize is not null)
                    {
                        window!.SetSize(lastAcceptedSize.Value);
                    }
                    else
                    {
                        lastAcceptedSize = window!.Size;
                        window.SetZoom((int)zoom);
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            app.MainWindow.ShowMessage("Fatal exception", error.ExceptionObject.ToString());
        };

        app.Run();
    }
}
