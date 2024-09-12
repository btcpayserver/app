using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
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
        using var node = await HeadlessTestNode.Create("Node1", _output);

        TestUtils.Eventually(
            () => Assert.Equal(BTCPayConnectionState.WaitingForAuth, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));

        
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.OnChainWalletManager.Generate());
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.LNManager.Generate());
        
        
        var username = Guid.NewGuid() + "@gg.com";

        Assert.True((await node.AccountManager.Register(btcpayUri.AbsoluteUri, username, username)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        await node.AccountManager.Logout();
        Assert.False(await node.AuthStateProvider.CheckAuthenticated());
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.GetAccount()?.RefreshToken);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Connecting, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsMaster, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, node.LNManager.State));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));
        Assert.Null(node.OnChainWalletManager.WalletConfig);

        await node.AccountManager.Logout();
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.GetAccount()?.RefreshToken);
        
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, node.LNManager.State));
        
        Assert.False(node.LNManager.CanConfigureLightningNode);
        await node.OnChainWalletManager.Generate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.OnChainWalletManager.Generate());

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, node.LNManager.State));
        
        Assert.NotNull(node.OnChainWalletManager.WalletConfig);
        Assert.NotNull(node.OnChainWalletManager.WalletConfig?.Derivations);
        Assert.False(node.OnChainWalletManager.WalletConfig?.Derivations.ContainsKey(WalletDerivation.LightningScripts));
        WalletDerivation? segwitDerivation = null;
        Assert.True(node.OnChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out segwitDerivation));
        Assert.False(string.IsNullOrEmpty(node.OnChainWalletManager.WalletConfig.Fingerprint));
        Assert.NotNull(segwitDerivation.Identifier);
        Assert.NotNull(segwitDerivation.Descriptor);
        Assert.NotNull(segwitDerivation.Name);
        
        
        Assert.True(node.LNManager.CanConfigureLightningNode);
        
        await node.LNManager.Generate(); 
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node.LNManager.State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.LNManager.Generate());
        WalletDerivation? lnDerivation = null;
        Assert.True(node.OnChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.LightningScripts, out lnDerivation));
        Assert.NotNull(lnDerivation.Identifier);
        Assert.Null(lnDerivation.Descriptor);
        Assert.NotNull(lnDerivation.Name);
        
        Assert.NotNull(node.LNManager.Node);
        Assert.NotNull(node.LNManager.Node.NodeId);
        
        
        using var node2 = await HeadlessTestNode.Create("Node2",_output);
        Assert.True((await node2.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node2.AuthStateProvider.CheckAuthenticated());
        
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.WaitingForEncryptionKey, node2.ConnectionManager.ConnectionState));
        Assert.False(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(new Mnemonic(Wordlist.English).ToString()));
        Assert.True(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(node2.OnChainWalletManager.WalletConfig!.Mnemonic));
        
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Syncing, node2.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsSlave, node2.ConnectionManager.ConnectionState));
        

    }
}