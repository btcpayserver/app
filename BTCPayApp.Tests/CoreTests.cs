using BTCPayApp.Core.Backup;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.Wallet;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.RPC;
using org.ldk.structs;
using Xunit.Abstractions;
using PeerInfo = BTCPayApp.Core.Data.PeerInfo;

namespace BTCPayApp.Tests;

public class CoreTests(ITestOutputHelper output)
{
    private string GetEnvironment(string variable, string defaultValue)
    {
        var var = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrEmpty(var) ? defaultValue : var;
    }

    [Fact]
    public async Task CanStartAppCore()
    {
        var btcpayUri = new Uri(GetEnvironment("BTCPAY_SERVER_URL", "https://localhost:14142"));
        using var node = await HeadlessTestNode.Create("Node1", output);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.WaitingForAuth, node.ConnectionManager.ConnectionState));
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
        Assert.NotNull(node.AccountManager.Account?.OwnerToken);

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node.ConnectionManager.ConnectionState), 30_000);
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));

        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));
        Assert.Null(await node.OnChainWalletManager.GetConfig());

        await node.AccountManager.Logout();
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));
        Assert.True((await node.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node.AuthStateProvider.CheckAuthenticated());
        Assert.NotNull(node.AccountManager.Account?.OwnerToken);

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.WaitingForConnection, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.NotConfigured, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.WaitingForConnection, node.LNManager.State));

        Assert.False(await node.LNManager.CanConfigureLightningNode());
        await node.OnChainWalletManager.Generate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.OnChainWalletManager.Generate());

        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.NotConfigured, node.LNManager.State));

        Assert.NotNull(await node.OnChainWalletManager.GetConfig());
        Assert.NotNull((await node.OnChainWalletManager.GetConfig())?.Derivations);
        Assert.False((await node.OnChainWalletManager.GetConfig())?.Derivations.ContainsKey(WalletDerivation.LightningScripts));
        var config = await node.OnChainWalletManager.GetConfig();
        Assert.NotNull(config);
        Assert.True(config.Derivations.TryGetValue(WalletDerivation.NativeSegwit, out var segwitDerivation));
        Assert.False(string.IsNullOrEmpty(config.Fingerprint));
        Assert.NotNull(segwitDerivation.Identifier);
        Assert.NotNull(segwitDerivation.Descriptor);
        Assert.NotNull(segwitDerivation.Name);

        Assert.True(await node.LNManager.CanConfigureLightningNode());

        await node.LNManager.Generate();
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node.LNManager.State));
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.LNManager.Generate());
        WalletDerivation? lnDerivation = null;
        Assert.True((await node.OnChainWalletManager.GetConfig())?.Derivations.TryGetValue(WalletDerivation.LightningScripts, out lnDerivation));
        Assert.NotNull(lnDerivation.Identifier);
        Assert.Null(lnDerivation.Descriptor);
        Assert.NotNull(lnDerivation.Name);

        Assert.NotNull(node.LNManager.Node);
        Assert.NotNull(node.LNManager.Node.NodeId);

        using var node2 = await HeadlessTestNode.Create("Node2", output);
        Assert.True((await node2.AccountManager.Login(btcpayUri.AbsoluteUri, username, username, null)).Succeeded);
        Assert.True(await node2.AuthStateProvider.CheckAuthenticated());

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.WaitingForEncryptionKey, node2.ConnectionManager.ConnectionState));
        Assert.False(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey(new Mnemonic(Wordlist.English).ToString()));
        Assert.True(await node2.App.Services.GetRequiredService<SyncService>().SetEncryptionKey((await node.OnChainWalletManager.GetConfig())!.Mnemonic));

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.Syncing, node2.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsSecondary, node2.ConnectionManager.ConnectionState));

        var address = await node.OnChainWalletManager.DeriveScript(WalletDerivation.NativeSegwit);

        await node.ConnectionManager.SwitchToSecondary();
        output.WriteLine("SLAVE CHECKPOINT");

        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsSecondary, node.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node2.ConnectionManager.ConnectionState));
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node2.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node2.LNManager.State));

        await node2.LNManager.Node.UpdateConfig(async config =>
        {
            config.AcceptInboundConnection = true;
            return (config, true);
        });
        await TestUtils.EventuallyAsync(async () => Assert.True((await node2.LNManager.Node.GetConfig()).AcceptInboundConnection));
        TestUtils.Eventually(() => Assert.NotNull(node2.LNManager.Node?.PeerHandler.Endpoint));

        //test onchain wallet
        var address2 = await node2.OnChainWalletManager.DeriveScript(WalletDerivation.NativeSegwit);
        var network = node.ConnectionManager.ReportedNetwork;
        Assert.NotNull(network);

        var rpc = new RPCClient(RPCCredentialString.Parse("server=http://localhost:43782;ceiwHEbqWI83:DwubwWsoo3"), network);
        Assert.NotNull(await FundWallet(rpc, address, Money.Coins(1)));
        Assert.NotNull(await FundWallet(rpc, address2, Money.Coins(1)));
        await TestUtils.EventuallyAsync(async () =>
        {
            var utxos = (await node2.OnChainWalletManager.GetUTXOS()).ToList();
            Assert.Equal(2, utxos.Count);
            Assert.Equal(Money.Coins(2).Satoshi, utxos.Sum(coin => (Money)coin.Amount));
        });

        Assert.Null(node2.AccountManager.CurrentStore);
        var store = await node2.AccountManager.GetClient().CreateStore(new CreateStoreRequest { Name = "Store1" });
        Assert.True(await node2.AccountManager.CheckAuthenticated(true));
        Assert.True((await node2.AccountManager.SetCurrentStoreId(store.Id)).Succeeded);
        Assert.Equal(store.Id, node2.AccountManager.CurrentStore?.Id);

        var res = await node2.AccountManager.TryApplyingAppPaymentMethodsToCurrentStore(node2.OnChainWalletManager, node2.LNManager, true, true);
        Assert.NotNull(res);
        Assert.True(await node2.OnChainWalletManager.IsOnChainOurs(res.Value.onchain));
        Assert.True(await node2.LNManager.IsLightningOurs(res.Value.lightning));

        var invoice = await node2.AccountManager.GetClient().CreateInvoice(store.Id, new CreateInvoiceRequest
        {
            Amount = 1m,
            Currency = "BTC"
        });
        var pms = await node2.AccountManager.GetClient().GetInvoicePaymentMethods(store.Id, invoice.Id);
        Assert.Equal(2, pms.Length);

        var pmOnchain = pms.First(p => p.PaymentMethodId == "BTC-CHAIN");
        var pmLN = pms.First(p => p.PaymentMethodId == "BTC-LN");

        var requestOfInvoice = Assert.Single(await node2.LNManager.Node.PaymentsManager.List(payments => payments));
        Assert.Equal(pmLN.Destination, requestOfInvoice.PaymentRequest.ToString());
        Assert.True(requestOfInvoice.Inbound);

        //let's pay onchain for now
        await FundWallet(rpc, BitcoinAddress.Create(pmOnchain.Destination, network), Money.Coins(1));
        await TestUtils.EventuallyAsync(async () =>
        {
            var utxos = (await node2.OnChainWalletManager.GetUTXOS()).ToList();
            Assert.Equal(3, utxos.Count);
            Assert.Equal(Money.Coins(3).Satoshi, utxos.Sum(coin => (Money)coin.Amount));
        });
        await TestUtils.EventuallyAsync(async () =>
        {
            requestOfInvoice = Assert.Single(await node2.LNManager.Node.PaymentsManager.List(payments => payments));
            Assert.Equal(pmLN.Destination, requestOfInvoice.PaymentRequest.ToString());
            //Note: this should be failed in the future. BTCPay should cancel the invoice once btcpay invoice is paid..
            Assert.Equal(LightningPaymentStatus.Pending, requestOfInvoice.Status);
        });

        //artificial self payment
        invoice = await node2.AccountManager.GetClient().CreateInvoice(store.Id, new CreateInvoiceRequest
        {
            Amount = 1m,
            Currency = "BTC"
        });

        pms = await node2.AccountManager.GetClient().GetInvoicePaymentMethods(store.Id, invoice.Id);
        Assert.Equal(2, pms.Length);

        pmLN = pms.First(p => p.PaymentMethodId == "BTC-LN");
        // AppLightningPayment node2PaymentUpdate = null;
        // node2.LNManager.Node.PaymentsManager.OnPaymentUpdate += (sender, payment) =>
        // {
        //     node2PaymentUpdate = (AppLightningPayment?) payment;
        //     return Task.CompletedTask;
        // };
        var outbound =
            await node2.LNManager.Node.PaymentsManager.PayInvoice(
                BOLT11PaymentRequest.Parse(pmLN.Destination, network));
        Assert.NotNull(outbound);
        Assert.Equal(pmLN.Destination, outbound.PaymentRequest.ToString());
        Assert.False(outbound.Inbound);
        Assert.Equal(LightningPaymentStatus.Complete, outbound.Status);

        await TestUtils.EventuallyAsync(async () =>
        {
            invoice = await node2.AccountManager.GetClient().GetInvoice(store.Id, invoice.Id);
            Assert.Equal(InvoiceStatus.Settled, invoice.Status);
        });
        var payments = await node2.LNManager.Node.PaymentsManager.List(payments => payments);
        Assert.Equal(3, payments.Count);
        Assert.Contains(payments,
            payment => payment.Inbound && payment.PaymentRequest.ToString() == pmLN.Destination &&
                       payment.Status == LightningPaymentStatus.Complete);
        Assert.Contains(payments,
            payment => !payment.Inbound && payment.PaymentRequest.ToString() == pmLN.Destination &&
                       payment.Status == LightningPaymentStatus.Complete);

        using var node3 = await HeadlessTestNode.Create("Node3", output);
        var username2 = Guid.NewGuid() + "@gg.com";
        Assert.True((await node3.AccountManager.Register(btcpayUri.AbsoluteUri, username2, username2)).Succeeded);
        Assert.True(await node3.AuthStateProvider.CheckAuthenticated());
        TestUtils.Eventually(() =>
            Assert.Equal(BTCPayConnectionState.ConnectedAsPrimary, node3.ConnectionManager.ConnectionState));
        await node3.OnChainWalletManager.Generate();
        await node3.LNManager.Generate();
        TestUtils.Eventually(() => Assert.Equal(OnChainWalletState.Loaded, node3.OnChainWalletManager.State));
        TestUtils.Eventually(() => Assert.Equal(LightningNodeState.Loaded, node3.LNManager.State));

        await node3.LNManager.Node.Peer(node2.LNManager.Node.NodeId, new PeerInfo
        {
            Label = "App2",
            Endpoint = node2.LNManager.Node.PeerHandler.Endpoint,
            Persistent = true
        });
        await TestUtils.EventuallyAsync(async () =>
        {
            Assert.Contains(await node3.LNManager.Node.GetPeers(), peer =>
                Convert.ToHexString(peer.get_counterparty_node_id()).Equals(node2.LNManager.Node.NodeId.ToHex(),
                    StringComparison.InvariantCultureIgnoreCase));

            Assert.Contains(await node2.LNManager.Node.GetPeers(), peer =>
                Convert.ToHexString(peer.get_counterparty_node_id()).Equals(node3.LNManager.Node.NodeId.ToHex(),
                    StringComparison.InvariantCultureIgnoreCase));

            Assert.True(
                (await node3.LNManager.Node.GetConfig()).Peers.TryGetValue(node2.LNManager.Node.NodeId.ToString(),
                    out var peerInfo));
            Assert.Equal("App2", peerInfo.Label);
            Assert.Equal(node2.LNManager.Node.PeerHandler.Endpoint.ToEndpointString(),
                peerInfo.Endpoint.ToEndpointString());
            Assert.True(peerInfo.Persistent);
            Assert.False(peerInfo.Trusted);
        });

        var channelMoney = Money.Coins(0.5m);
        var result = await node2.LNManager.Node.OpenChannel(channelMoney, node3.LNManager.Node.NodeId);
        _ = Convert
            .ToHexString(Assert.IsType<Result_ChannelIdAPIErrorZ.Result_ChannelIdAPIErrorZ_OK>(result).res.get_a())
            .ToLowerInvariant();
        await TestUtils.EventuallyAsync(async () =>
        {
            var node2Channel = Assert.Single(await node2.LNManager.Node.GetChannels());
            var node3Channel = Assert.Single(await node3.LNManager.Node.GetChannels());

            Assert.Equal(node2Channel.channel.Id, node3Channel.channel.Id);
            Assert.Equal(
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node3Channel.channelDetails.get_confirmations()).some,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node2Channel.channelDetails.get_confirmations())
                    .some);

            Assert.Equal(0,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node3Channel.channelDetails.get_confirmations())
                    .some);

            Assert.False(node3Channel.channelDetails.get_is_channel_ready());
            Assert.False(node2Channel.channelDetails.get_is_channel_ready());
            Assert.False(node3Channel.channelDetails.get_is_usable());
            Assert.False(node2Channel.channelDetails.get_is_usable());

            Assert.Equal(1,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(
                        node3Channel.channelDetails.get_confirmations_required())
                    .some);
            Assert.Equal(1,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(
                        node2Channel.channelDetails.get_confirmations_required())
                    .some);
        });

        await rpc.GenerateAsync(1);
        await TestUtils.EventuallyAsync(async () =>
        {
            var node2Channel = Assert.Single(await node2.LNManager.Node.GetChannels() ?? []);
            var node3Channel = Assert.Single(await node3.LNManager.Node.GetChannels() ?? []);
            var node2ChannelReserve = node2Channel.channelDetails?.get_unspendable_punishment_reserve() is Option_u64Z.Option_u64Z_Some amtX ? amtX.some : 0;

            Assert.Equal(node2Channel.channel.Id, node3Channel.channel.Id);
            Assert.Equal(
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node3Channel.channelDetails?.get_confirmations()).some,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node2Channel.channelDetails?.get_confirmations())
                    .some);

            Assert.True(node3Channel.channelDetails.get_is_channel_ready());
            Assert.True(node2Channel.channelDetails.get_is_channel_ready());
            Assert.True(node3Channel.channelDetails.get_is_usable());
            Assert.True(node2Channel.channelDetails.get_is_usable());

            Assert.Equal(1,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node3Channel.channelDetails.get_confirmations_required()).some);
            Assert.Equal(1,
                Assert.IsType<Option_u32Z.Option_u32Z_Some>(node2Channel.channelDetails.get_confirmations_required()).some);
            Assert.Equal(channelMoney.Satoshi, node2Channel.channelDetails.get_channel_value_satoshis());
            Assert.Equal(LightMoney.Satoshis(channelMoney.Satoshi - node2ChannelReserve).MilliSatoshi, node2Channel.channelDetails.get_outbound_capacity_msat());
            Assert.Equal(0, node3Channel.channelDetails.get_outbound_capacity_msat());
        });
        var node3PR = await node3.LNManager.Node.PaymentsManager.RequestPayment(LightMoney.Coins(0.01m),
            TimeSpan.FromHours(2),
            uint256.Zero);

        await TestUtils.EventuallyAsync(async () =>
        {
            var node2PR = await node2.LNManager.Node.PaymentsManager.PayInvoice(node3PR.PaymentRequest);
            while (node2PR.Status == LightningPaymentStatus.Pending)
            {
                node2PR = (await node2.LNManager.Node.PaymentsManager.List(payments =>
                    payments.Where(payment =>
                        node2PR.Inbound == payment.Inbound &&
                        payment.PaymentHash == node2PR.PaymentHash &&
                        payment.PaymentId == node2PR.PaymentId
                    ))).Single();
                await Task.Delay(500);
            }

            Assert.Equal(LightningPaymentStatus.Complete, node2PR.Status);
            Assert.Equal(node2PR.Preimage, node3PR.Preimage);
            Assert.NotNull(node2PR.Preimage);
            Assert.Single(await node3.LNManager.Node.PaymentsManager.List(payments =>
                payments.Where(payment =>
                    payment.PaymentHash == node3PR.PaymentHash && payment.Inbound &&
                    payment.Status == LightningPaymentStatus.Complete)));
        });
    }

    private async Task<uint256?> FundWallet(RPCClient rpc, BitcoinAddress address, Money m)
    {
        var attempts = 0;
        while (attempts < 101)
            try
            {
                return await rpc.SendToAddressAsync(address, m);
            }
            catch (Exception)
            {
                attempts++;
                await rpc.GenerateAsync(1);
            }

        return null;
    }
}
