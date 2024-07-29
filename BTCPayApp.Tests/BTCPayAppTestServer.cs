using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NBitcoin;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BTCPayApp.Tests;

class BTCPayAppTestServer : BaseWebApplicationFactory<Program>
{
    public BTCPayAppTestServer(ITestOutputHelper output, bool newDir = true, Dictionary<string, string>? config = null) : base(output, config)
    {
        if (newDir)
        {
            Config.AddOrReplace("BTCPAYAPP_DIRNAME", "btcpayserver-test-" + RandomUtils.GetUInt32());
        }
    }
}

class BaseWebApplicationFactory<T> : WebApplicationFactory<T> where T : class
{
    protected IHost? Host;
    protected readonly ITestOutputHelper Output;
    protected readonly Dictionary<string, string> Config;
    protected readonly Task PlaywrightInstallTask;

    public string ServerAddress
    {
        get
        {
            if (Host is null)
            {
                CreateDefaultClient();
            }

            return ClientOptions.BaseAddress.ToString();
        }
    }

    public BaseWebApplicationFactory(ITestOutputHelper output,  Dictionary<string, string>? config = null)
    {
        Output = output;

        Config = config ?? new();


        PlaywrightInstallTask ??= Task.Run(InstallPlaywright);
    }

    public class LifetimeBridge
    {

        public LifetimeBridge(IHostApplicationLifetime  lifetime, IServer server, TaskCompletionSource<string[]> tcs )
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                tcs.SetResult(server.Features.Get<IServerAddressesFeature>()!.Addresses.ToArray());
            });
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Create the host that is actually used by the
        // TestServer (In Memory).
        var testHost = base.CreateHost(builder);


        TaskCompletionSource<string[]> tcs = new TaskCompletionSource<string[]>();
        builder.ConfigureWebHost(webHostBuilder => webHostBuilder.UseKestrel().UseUrls("https://127.0.0.1:0").ConfigureServices(collection => collection.AddSingleton<LifetimeBridge>(provider => new LifetimeBridge(provider.GetRequiredService<IHostApplicationLifetime>(), provider.GetRequiredService<IServer>(), tcs))));
        // configure and start the actual host using Kestrel.
        Host = builder.Build();
        Host.Start();
        Host.Services.GetRequiredService<LifetimeBridge>();
        // Extract the selected dynamic port out of the Kestrel server
        // and assign it onto the client options for convenience so it
        // "just works" as otherwise it'll be the default http://localhost
        // URL, which won't route to the Kestrel-hosted HTTP server.
        var addresses = tcs.Task.GetAwaiter().GetResult();

        ClientOptions.BaseAddress = new Uri(addresses.Last().Replace("127.0.0.1", "localhost"));

        testHost.Start();
        return testHost;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder
            .ConfigureAppConfiguration(configurationBuilder =>
                configurationBuilder.AddInMemoryCollection(Config!))
            .ConfigureLogging(
                logging =>
                {
                    // check if scopes are used in normal operation
                    var useScopes = logging.UsesScopes();
                    // remove other logging providers, such as remote loggers or unnecessary event logs
                    logging.ClearProviders();
                    logging.Services.AddSingleton<ILoggerProvider>(_ =>
                        new WebApplicationFactoryExtensions.XunitLoggerProvider(Output, useScopes));
                });
    }

    private static void InstallPlaywright()
    {
        Microsoft.Playwright.Program.Main(new[] {"install", "--with-deps"});
    }

    public async Task<IBrowserContext> InitializeAsync()
    {
        Assert.NotNull(ServerAddress);
        await PlaywrightInstallTask;
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
        });

        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
            IsMobile = true
        });
    }

    public IPlaywright? Playwright { get; set; }
    public IBrowser? Browser { get; set; }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Playwright != null)
        {
            if (Browser != null)
            {
                await Browser.DisposeAsync();
            }

            Playwright.Dispose();
            Playwright = null;
        }
    }

    public void Eventually(Action act, int ms = 20_000)
    {
        var cts = new CancellationTokenSource(ms);
        while (true)
        {
            try
            {
                act();
                break;
            }
            catch (PlaywrightException) when (!cts.Token.IsCancellationRequested)
            {
                cts.Token.WaitHandle.WaitOne(500);
            }
            catch (XunitException) when (!cts.Token.IsCancellationRequested)
            {
                cts.Token.WaitHandle.WaitOne(500);
            }
        }
    }

    public static async Task EventuallyAsync(Func<Task> act, int delay = 20000)
    {
        CancellationTokenSource cts = new CancellationTokenSource(delay);
        while (true)
        {
            try
            {
                await act();
                break;
            }
            catch (PlaywrightException) when (!cts.Token.IsCancellationRequested)
            {
                bool timeout =false;
                try
                {
                    await Task.Delay(500, cts.Token);
                }
                catch { timeout = true; }
                if (timeout)
                    throw;
            }
            catch (XunitException) when (!cts.Token.IsCancellationRequested)
            {
                bool timeout =false;
                try
                {
                    await Task.Delay(500, cts.Token);
                }
                catch { timeout = true; }
                if (timeout)
                    throw;
            }
        }
    }
}
