using System.Drawing;
using Comrade.Core;
using Comrade.Desktop;
using Comrade.UI;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using PhotinoNET;

namespace Comrade.Photino;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        builder.Services.AddComradeUIServices();
        builder.Services.ConfigureComradeCore();
        builder.Services.ConfigureComradeDesktop();
        builder.Services.AddLogging();
        builder.RootComponents.Add<App>("app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        var app = builder.Build();

        // customize window.
        app.MainWindow
            .SetResizable(true)
            .SetZoom(0)
            // .SetIconFile("favicon.ico")
            .SetTitle("Comrade");

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
            catch (Exception exc)
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
