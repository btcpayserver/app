using System.Diagnostics;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Extensions;
using BTCPayApp.Core.Wallet;
using BTCPayApp.Desktop;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plugin.Fingerprint.Abstractions;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public class TestLoggerFactory(string prefix, ILoggerFactory inner) : ILoggerFactory
{
    public void Dispose()
    {
        inner.Dispose();
    }

    public void AddProvider(ILoggerProvider provider)
    {
        inner.AddProvider(provider);
    }

    public ILogger CreateLogger(string categoryName)
    {
       return inner.CreateLogger(prefix + ":" + categoryName);
    }
}

public class HeadlessTestNode : IDisposable
{
    public static async Task<HeadlessTestNode> Create(string nodeName, ITestOutputHelper testOutputHelper, string[]? args = null)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var res = new HeadlessTestNode(nodeName, testOutputHelper, args);
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Init, res.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Init, res.LNManager.State));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Init, res.OnChainWalletManager.State));

        var appTask = res.App.StartAsync();
        await Task.WhenAny(appTask, res.App.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.AsTask());
        await appTask;
        return res;
    }

    private HeadlessTestNode(string nodeName, ITestOutputHelper testOutputHelper, string[]? args = null)
    {
        var x = Path.Combine("btcpaytests", $"{nodeName}-{Guid.NewGuid()}");
        var hostBuilder = Host.CreateDefaultBuilder();
        hostBuilder.ConfigureServices(collection =>
        {
            collection.AddDataProtection(options => { options.ApplicationDiscriminator = "BTCPayApp"; });
            collection.AddSingleton<ISecureConfigProvider, DesktopSecureConfigProvider>();
            collection.AddSingleton<IFingerprint, StubFingerprintProvider>();
            collection.AddSingleton<IDataDirectoryProvider, DesktopDataDirectoryProvider>();
            collection.ConfigureBTCPayAppCore();
            collection.Replace(ServiceDescriptor.Singleton<ILoggerFactory, TestLoggerFactory>(provider => new TestLoggerFactory(nodeName, ActivatorUtilities.CreateInstance<LoggerFactory>(provider))));
        });
        hostBuilder
            .ConfigureLogging(builder =>
            {
                // builder.Services.Replace(ServiceDescriptor.Singleton<ILoggerFactory, TestLoggerFactory>(provider => new TestLoggerFactory(nodeName)));
                // check if scopes are used in normal operation
                var useScopes = builder.UsesScopes();
                // remove other logging providers, such as remote loggers or unnecessary event logs
                /*builder.ClearProviders();
                builder.AddConsole();
                builder.AddDebug();*/
                builder.Services.AddSingleton<ILoggerProvider>(_ => new WebApplicationFactoryExtensions.XunitLoggerProvider(testOutputHelper, useScopes));
            })
            .ConfigureAppConfiguration(builder =>
                builder.AddEnvironmentVariables()
                    .AddInMemoryCollection([
                        new KeyValuePair<string, string?>("BTCPAYAPP_DIRNAME", x)
                    ]));
        App = hostBuilder.Build();
    }

    public BTCPayConnectionManager ConnectionManager => App.Services.GetRequiredService<BTCPayConnectionManager>();
    public LightningNodeManager LNManager => App.Services.GetRequiredService<LightningNodeManager>();
    public OnChainWalletManager OnChainWalletManager => App.Services.GetRequiredService<OnChainWalletManager>();
    public IAccountManager AccountManager => App.Services.GetRequiredService<IAccountManager>();
    public AuthStateProvider AuthStateProvider => App.Services.GetRequiredService<AuthStateProvider>();

    public IHost App { get; }

    public void Dispose()
    {
        var dir = App.Services.GetRequiredService<IDataDirectoryProvider>().GetAppDataDirectory().ConfigureAwait(false)
            .GetAwaiter().GetResult();

        Stopwatch sw = new();
        sw.Start();
        App.StopAsync().GetAwaiter().GetResult();
        App.Dispose();
        sw.Stop();
        Console.WriteLine($"App stopped in {sw.ElapsedMilliseconds}ms");
        // while (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
               // Thread.Sleep(500);
            }
        }
    }
}
