using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public class CoreTests
{
    private readonly ITestOutputHelper _output;

    public CoreTests(ITestOutputHelper output)
    {
        _output = output;
    }



    [Fact]
    public async Task CanStartAppCore()
    {
        var btcpayUri = new Uri("https://localhost:14142");
        using var node = new HeadlessTestNode(_output);
        var connectionManager = node.App.Services.GetRequiredService<BTCPayConnectionManager>();
        var lnManager = node.App.Services.GetRequiredService<LightningNodeManager>();
        var onChainWalletManager = node.App.Services.GetRequiredService<OnChainWalletManager>();
        var accountManager = node.App.Services.GetRequiredService<IAccountManager>();
        var authStateProvider = node.App.Services.GetRequiredService<AuthStateProvider>();

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Init, connectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Init, lnManager.State));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Init, onChainWalletManager.State));


        var appTask = node.App.StartAsync();
        //throw is this task faults in the bg

        await Task.WhenAny(appTask,
            node.App.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.AsTask());

        TestUtils.Eventually(
            () => Assert.Equal(BTCPayConnectionState.WaitingForAuth, connectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.WaitingForConnection, onChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, lnManager.State));

        
        await Assert.ThrowsAsync<InvalidOperationException>(() => onChainWalletManager.Generate());
        await Assert.ThrowsAsync<InvalidOperationException>(() => lnManager.Generate());
        
        
        var username = Guid.NewGuid() + "@gg.com";

        Assert.True((await accountManager.Register(btcpayUri.AbsoluteUri, username, username)).Succeeded);
        Assert.True(await authStateProvider.CheckAuthenticated());
        await accountManager.Logout();
        Assert.False(await authStateProvider.CheckAuthenticated());
        Assert.True((await accountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await authStateProvider.CheckAuthenticated());
        Assert.NotNull(accountManager.GetAccount()?.RefreshToken);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Connecting, connectionManager.ConnectionState));
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsMaster, connectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, lnManager.State));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, onChainWalletManager.State));
        Assert.Null(onChainWalletManager.WalletConfig);

        Assert.False(lnManager.CanConfigureLightningNode);
        await onChainWalletManager.Generate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => onChainWalletManager.Generate());

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, onChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, lnManager.State));
        
        Assert.NotNull(onChainWalletManager.WalletConfig);
        Assert.NotNull(onChainWalletManager.WalletConfig?.Derivations);
        Assert.False(onChainWalletManager.WalletConfig?.Derivations.ContainsKey(WalletDerivation.LightningScripts));
        WalletDerivation? segwitDerivation = null;
        Assert.True(onChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out segwitDerivation));
        Assert.False(string.IsNullOrEmpty(onChainWalletManager.WalletConfig.Fingerprint));
        Assert.NotNull(segwitDerivation.Identifier);
        Assert.NotNull(segwitDerivation.Descriptor);
        Assert.NotNull(segwitDerivation.Name);
        
       
        Assert.True(lnManager.CanConfigureLightningNode);
        
        
        await lnManager.Generate(); 
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, lnManager.State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => lnManager.Generate());
        
        
    }
}