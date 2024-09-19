using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.RPC;
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
        TestUtils.Eventually(() =>
            Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
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

        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.Connecting, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsMaster, node.ConnectionManager.ConnectionState)); 
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));

        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));
           Assert.Null(node.OnChainWalletManager.WalletConfig);

        await node.AccountManager.Logout();
        TestUtils.Eventually(() =>
            Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.GetAccount()?.RefreshToken);

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));

        Assert.False(node.LNManager.CanConfigureLightningNode);
        await node.OnChainWalletManager.Generate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.OnChainWalletManager.Generate());

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, node.LNManager.State));

        Assert.NotNull(node.OnChainWalletManager.WalletConfig);
        Assert.NotNull(node.OnChainWalletManager.WalletConfig?.Derivations);
        Assert.False(
            node.OnChainWalletManager.WalletConfig?.Derivations.ContainsKey(WalletDerivation.LightningScripts));
        WalletDerivation? segwitDerivation = null;
        Assert.True(
            node.OnChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.NativeSegwit,
                out segwitDerivation));
        Assert.False(string.IsNullOrEmpty(node.OnChainWalletManager.WalletConfig.Fingerprint));
        Assert.NotNull(segwitDerivation.Identifier);
        Assert.NotNull(segwitDerivation.Descriptor);
        Assert.NotNull(segwitDerivation.Name);


        Assert.True(node.LNManager.CanConfigureLightningNode);

        await node.LNManager.Generate();
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node.LNManager.State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.LNManager.Generate());
        WalletDerivation? lnDerivation = null;
        Assert.True(
            node.OnChainWalletManager.WalletConfig?.Derivations.TryGetValue(WalletDerivation.LightningScripts,
                out lnDerivation));
        Assert.NotNull(lnDerivation.Identifier);
        Assert.Null(lnDerivation.Descriptor);
        Assert.NotNull(lnDerivation.Name);

        Assert.NotNull(node.LNManager.Node);
        Assert.NotNull(node.LNManager.Node.NodeId);


        using var node2 = await HeadlessTestNode.Create("Node2", _output);
        Assert.True((await node2.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node2.AuthStateProvider.CheckAuthenticated());

        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.WaitingForEncryptionKey, node2.ConnectionManager.ConnectionState));
        Assert.False(await node2.App.Services.GetRequiredService<SyncService>()
            .SetEncryptionKey(new Mnemonic(Wordlist.English).ToString()));
        Assert.True(await node2.App.Services.GetRequiredService<SyncService>()
            .SetEncryptionKey(node.OnChainWalletManager.WalletConfig!.Mnemonic));

        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.Syncing, node2.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsSlave, node2.ConnectionManager.ConnectionState));


        await node.ConnectionManager.SwitchToSlave();
        _output.WriteLine("SLAVE CHECKPOINT");
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsSlave, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsMaster, node2.ConnectionManager.ConnectionState));

        
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node2.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node2.LNManager.State));
        

        await node2.LNManager.Node.UpdateConfig(async config =>
        {
            config.AcceptInboundConnection = true;
            return (config, true);
        });
        await TestUtils.EventuallyAsync(async () =>
            Assert.True((await node2.LNManager.Node.GetConfig()).AcceptInboundConnection));
        TestUtils.Eventually(() => Assert.NotNull(node2.LNManager.Node?.PeerHandler.Endpoint));
        
        //test onchain wallet
        var address = await node.OnChainWalletManager.DeriveScript(WalletDerivation.NativeSegwit);
        var address2 = await node2.OnChainWalletManager.DeriveScript(WalletDerivation.NativeSegwit);
    
        var network = node.ConnectionManager.ReportedNetwork;
        Assert.NotNull(network);
        var rpc = new RPCClient(RPCCredentialString.Parse("server=http://localhost:43782;ceiwHEbqWI83:DwubwWsoo3"), network);
        Assert.NotNull(await FundWallet(rpc, address.GetDestinationAddress(network), Money.Coins(1)));
        Assert.NotNull(await FundWallet(rpc, address2.GetDestinationAddress(network), Money.Coins(1)));
        await TestUtils.EventuallyAsync(async () =>
        {
            var utxos = await node.OnChainWalletManager.GetUTXOS();
            Assert.Equal(2, utxos.Count());

            Assert.Equal(Money.Coins(2).Satoshi, utxos.Sum(coin => (Money) coin.Amount));
        });
      
       Assert.Null( node2.AccountManager.GetCurrentStore());
       var store = await node2.AccountManager.GetClient().CreateStore(new CreateStoreRequest()
       {
           Name = "Store1"
       });
       Assert.True((await node2.AccountManager.SetCurrentStoreId(store.Id)).Succeeded);
         Assert.Equal(store.Id, node2.AccountManager.GetCurrentStore()?.Id);

         var res = await node2.AccountManager.TryApplyingAppPaymentMethodsToCurrentStore(node2.OnChainWalletManager, node2.LNManager, true, true);
         Assert.NotNull(res);
         Assert.True(node2.OnChainWalletManager.IsOnChainOurs(res.Value.onchain));
         Assert.True(node2.LNManager.IsLightningOurs(res.Value.lightning));

         var invoice = await node2.AccountManager.GetClient().CreateInvoice(store.Id, new CreateInvoiceRequest()
         {
             Amount = 1m,
             Currency = "BTC",
         });
         var pms = await  node2.AccountManager.GetClient().GetInvoicePaymentMethods(store.Id, invoice.Id);
         Assert.Equal(2, pms.Length);
         
         var pmOnchain = pms.First(p => p.PaymentMethodId == "BTC");
         var pmLN = pms.First(p => p.PaymentMethodId == "BTC-LN");

         var requestOfInvoice =
             Assert.Single(await node2.App.Services.GetRequiredService<PaymentsManager>().List(payments => payments));
         Assert.Equal(pmLN.Destination, requestOfInvoice.PaymentRequest.ToString());
         Assert.True(requestOfInvoice.Inbound);
         
         //let's pay onchain for now
         await FundWallet(rpc, BitcoinAddress.Create(pmOnchain.Destination, network), Money.Coins(1));
         await TestUtils.EventuallyAsync(async () =>
         {
             var utxos = await node.OnChainWalletManager.GetUTXOS();
             Assert.Equal(3, utxos.Count());

             Assert.Equal(Money.Coins(3).Satoshi, utxos.Sum(coin => (Money) coin.Amount));
         });
         await TestUtils.EventuallyAsync(async () =>
         {
             requestOfInvoice =
                 Assert.Single(
                     await node2.App.Services.GetRequiredService<PaymentsManager>().List(payments => payments));
             Assert.Equal(pmLN.Destination, requestOfInvoice.Status.ToString());

         });
         
         
         
        
        using var node3 = await HeadlessTestNode.Create("Node3", _output);
        var username2 = Guid.NewGuid() + "@gg.com";
        Assert.True((await node3.AccountManager.Register(btcpayUri.AbsoluteUri, username2, username2)).Succeeded);
        Assert.True(await node3.AuthStateProvider.CheckAuthenticated());
        await node3.OnChainWalletManager.Generate();
        await node3.LNManager.Generate();
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node3.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node3.LNManager.State));
        
    }
    
    private async Task<uint256?> FundWallet(RPCClient rpc, BitcoinAddress address, Money m)
    {
        
        var attempts = 0;
        while (attempts < 101)
        {
            try
            {
                return await rpc.SendToAddressAsync(address, m);
            }
            catch (Exception ex)
            {
                attempts++;
                await rpc.GenerateAsync(1);
            }
        }

        return null;
    }
}