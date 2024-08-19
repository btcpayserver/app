using System.Net;
using System.Net.Sockets;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LSP.JIT;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBitcoin;
using org.ldk.structs;
using Network = NBitcoin.Network;
using Script = NBitcoin.Script;
using SocketAddress = org.ldk.structs.SocketAddress;
using TxOut = NBitcoin.TxOut;

namespace BTCPayApp.Core.LDK;

public static class LDKExtensions
{
    public static int? Get(this Option_u32Z option)
    {
        return option is Option_u32Z.Option_u32Z_Some some ? some.some : null;
    }

    public static string GetError(this APIError apiError)
    {
        return apiError switch
        {
            APIError.APIError_APIMisuseError a => a.err,
            APIError.APIError_FeeRateTooHigh b => $"{b.err} feerate:{b.feerate}",
            APIError.APIError_InvalidRoute c => c.err,
            APIError.APIError_ChannelUnavailable d => d.err,
            APIError.APIError_MonitorUpdateInProgress e => "Monitor update in progress",
            APIError.APIError_IncompatibleShutdownScript f => "Incompatible shutdown script",
            _ => throw new ArgumentOutOfRangeException(nameof(apiError))
        };
    }

    public static Option_SocketAddressZ GetSocketAddress(this Socket socket)
    {
        if (socket.RemoteEndPoint is null)
        {
            return Option_SocketAddressZ.none();
        }

        var remote = socket.RemoteEndPoint?.ToString();

        if (remote is null)
            return Option_SocketAddressZ.none();
        var ipe = ((IPEndPoint) socket.RemoteEndPoint);

        // return Option_SocketAddressZ.some(SocketAddress.tcp_ip_v4(ipe.Address.GetAddressBytes(), (short) ipe.Port));
        var socketAddress = SocketAddress.from_str(remote);
        return !socketAddress.is_ok()
            ? Option_SocketAddressZ.none()
            : Option_SocketAddressZ.some(
                ((Result_SocketAddressSocketAddressParseErrorZ.Result_SocketAddressSocketAddressParseErrorZ_OK)
                    socketAddress).res);
    }

    public static SocketAddress? Endpoint(this EndPoint endPoint)
    {
        return SocketAddress.from_str(endPoint.ToString()) switch
        {
            org.ldk.structs.Result_SocketAddressSocketAddressParseErrorZ.Result_SocketAddressSocketAddressParseErrorZ_OK
                ok => ok.res,
            _ => null
        };
    }

    public class LDKEntropySource : EntropySourceInterface
    {
        public byte[] get_secure_random_bytes()
        {
            return RandomUtils.GetBytes(32);
        }
    }

    public static IServiceCollection AddLDK(this IServiceCollection services)
    {
        services.AddScoped<KeysManager>(provider => KeysManager.of(provider.GetRequiredService<LDKNode>().Seed,
            DateTimeOffset.Now.ToUnixTimeSeconds(),
            RandomUtils.GetInt32()));
        services.AddScoped(provider => provider.GetRequiredService<KeysManager>().as_NodeSigner());
        services.AddScoped(provider => provider.GetRequiredService<KeysManager>().as_OutputSpender());
        services.AddScoped<LDKPersister>();
        services.AddScoped<Persister>(provider =>
            Persister.new_impl(provider.GetRequiredService<LDKPersister>()));
        services.AddScoped(provider =>
            provider.GetRequiredService<LDKNode>().GetConfig().GetAwaiter().GetResult().AsLDKUserConfig());
        services.AddScoped(provider => provider.GetRequiredService<OnChainWalletManager>().Network!);

        services.AddScoped(provider =>
        {
            var feeEstimator = provider.GetRequiredService<FeeEstimator>();
            var watch = provider.GetRequiredService<Watch>();
            var broadcasterInterface = provider.GetRequiredService<BroadcasterInterface>();
            var router = provider.GetRequiredService<Router>();
            var logger = provider.GetRequiredService<Logger>();
            var signerProvider = provider.GetRequiredService<SignerProvider>();
            var userConfig = provider.GetRequiredService<UserConfig>();
            var entropySource = provider.GetRequiredService<EntropySource>();
            var nodeSigner = provider.GetRequiredService<NodeSigner>();
            var chainParameters = provider.GetRequiredService<ChainParameters>();
            var filter = provider.GetRequiredService<Filter>();
            var node = provider.GetRequiredService<LDKNode>();
            var channelManagerSerialized = node
                .GetSerializedChannelManager(entropySource, signerProvider)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            if (channelManagerSerialized is not null)
            {
                return ChannelManagerHelper.Load(channelManagerSerialized.Value.channelMonitors,
                    channelManagerSerialized.Value.serializedChannelManager, entropySource, signerProvider, nodeSigner,
                    feeEstimator, watch, broadcasterInterface, router, logger, userConfig, filter);
            }

            return ChannelManager.of(feeEstimator, watch, broadcasterInterface, router, logger, entropySource,
                nodeSigner, signerProvider, userConfig, chainParameters,
                (int) DateTimeOffset.Now.ToUnixTimeSeconds());
        });
        services.AddScoped(provider => provider.GetRequiredService<ChannelManager>().as_ChannelMessageHandler());
        services.AddScoped(provider => provider.GetRequiredService<ChannelManager>().as_OffersMessageHandler());
        services.AddScoped<LDKNode>();
        services.AddScoped(provider => P2PGossipSync.of(provider.GetRequiredService<NetworkGraph>(),
            Option_UtxoLookupZ.none(), provider.GetRequiredService<Logger>()));
        services.AddScoped(provider => GossipSync.p2_p(provider.GetRequiredService<P2PGossipSync>()));
        services.AddScoped(provider => DefaultMessageRouter.of(provider.GetRequiredService<NetworkGraph>(),
            provider.GetRequiredService<EntropySource>()));
        services.AddScoped(provider => provider.GetRequiredService<P2PGossipSync>().as_RoutingMessageHandler());
        services.AddScoped(provider => provider.GetRequiredService<DefaultMessageRouter>().as_MessageRouter());
        services.AddScoped(provider => IgnoringMessageHandler.of().as_CustomOnionMessageHandler());
        services.AddScoped(provider => IgnoringMessageHandler.of().as_CustomMessageHandler());
        services.AddScoped<NodeIdLookUp>(provider => EmptyNodeIdLookUp.of().as_NodeIdLookUp());
        services.AddScoped<OnionMessenger>(provider =>
            OnionMessenger.of(
                provider.GetRequiredService<EntropySource>(),
                provider.GetRequiredService<NodeSigner>(),
                provider.GetRequiredService<Logger>(),
                provider.GetRequiredService<NodeIdLookUp>(),
                provider.GetRequiredService<MessageRouter>(),
                provider.GetRequiredService<OffersMessageHandler>(),
                provider.GetRequiredService<CustomOnionMessageHandler>()));


        services.AddScoped(provider => provider.GetRequiredService<OnionMessenger>().as_OnionMessageHandler());
        services.AddScoped<LDKBroadcaster>();
        services.AddScoped<PeerManager>(provider => PeerManager.of(
            provider.GetRequiredService<ChannelMessageHandler>(),
            provider.GetRequiredService<RoutingMessageHandler>(),
            provider.GetRequiredService<OnionMessageHandler>(),
            provider.GetRequiredService<CustomMessageHandler>(),
            DateTime.Now.ToUnixTimestamp(),
            RandomUtils.GetBytes(32),
            provider.GetRequiredService<Logger>(),
            provider.GetRequiredService<NodeSigner>()));
        services.AddScoped<BroadcasterInterface>(provider =>
            BroadcasterInterface.new_impl(provider.GetRequiredService<LDKBroadcaster>()));
        services.AddScoped<LDKCoinSelector>();
        services.AddScoped<CoinSelectionSource>(provider =>
            CoinSelectionSource.new_impl(provider.GetRequiredService<LDKCoinSelector>()));
        services.AddScoped<BumpTransactionEventHandler>(provider =>
            BumpTransactionEventHandler.of(provider.GetRequiredService<BroadcasterInterface>(),
                provider.GetRequiredService<CoinSelectionSource>(), provider.GetRequiredService<SignerProvider>(),
                provider.GetRequiredService<Logger>()));

        services.AddLDKEventHandler<LDKBumpTransactionEventHandler>();
        services.AddLDKEventHandler<LDKFundingGenerationReadyEventHandler>();
        services.AddLDKEventHandler<LDKSpendableOutputEventHandler>();
        services.AddLDKEventHandler<LDKOpenChannelRequestEventHandler>();
        services.AddLDKEventHandler<PaymentsManager>();
        services.AddLDKEventHandler<LDKPendingHTLCsForwardableEventHandler>();
        services.AddLDKEventHandler<LDKAnnouncementBroadcaster>();
        services.AddLDKEventHandler<LDKNode>();

        services.AddScoped<LDKEventHandler>();
        services.AddScoped<org.ldk.structs.EventHandler>(provider =>
            org.ldk.structs.EventHandler.new_impl(provider.GetRequiredService<LDKEventHandler>()));
        services.AddScoped<LDKFeeEstimator>();
        services.AddScoped<LDKWalletLoggerFactory>();
        services.AddScoped<FeeEstimator>(provider =>
            FeeEstimator.new_impl(provider.GetRequiredService<LDKFeeEstimator>()));
        services.AddScoped<LDKPersistInterface>();
        services.AddScoped<Persist>(provider => Persist.new_impl(provider.GetRequiredService<LDKPersistInterface>()));
        services.AddScoped<RapidGossipSync>(provider => RapidGossipSync.of(provider.GetRequiredService<NetworkGraph>(),
            provider.GetRequiredService<Logger>()));
        services.AddScoped<LDKSignerProvider>();
        services.AddScoped<SignerProvider>(provider =>
            SignerProvider.new_impl(provider.GetRequiredService<LDKSignerProvider>()));
        services.AddScoped<ChainMonitor>(provider =>
            ChainMonitor.of(
                Option_FilterZ.some(provider.GetRequiredService<Filter>()),
                provider.GetRequiredService<BroadcasterInterface>(),
                provider.GetRequiredService<Logger>(),
                provider.GetRequiredService<FeeEstimator>(),
                provider.GetRequiredService<Persist>()
            ));
        services.AddScoped<Watch>(provider => provider.GetRequiredService<ChainMonitor>().as_Watch());
        services.AddScoped<LDKFilter>();
        services.AddScoped<LDKChangeDestinationSource>();
        services.AddScoped<LDKKVStore>();
        services.AddScoped<KVStore>(provider => KVStore.new_impl(provider.GetRequiredService<LDKKVStore>()));
        services.AddScoped<ChangeDestinationSource>(provider => ChangeDestinationSource.new_impl(provider.GetRequiredService<LDKChangeDestinationSource>()));
        services.AddScoped<Filter>(provider => Filter.new_impl(provider.GetRequiredService<LDKFilter>()));
        services.AddScoped<Confirm, Confirm>(provider => provider.GetRequiredService<ChannelManager>().as_Confirm());
        services.AddScoped<Confirm, Confirm>(provider => provider.GetRequiredService<ChainMonitor>().as_Confirm());
        services.AddScoped<Confirm, Confirm>(provider => provider.GetRequiredService<OutputSweeper>().as_Confirm());
        services.AddScoped<LDKChannelSync>();
        services.AddScoped<LDKPeerHandler>();
        services.AddScoped<LDKBackgroundProcessor>();
        services.AddScoped<LDKAnnouncementBroadcaster>();
        services.AddScoped<PaymentsManager>();
        services.AddScoped<BTCPayPaymentsNotifier>();
        services.AddScoped<BTCPayPaymentsNotifier>();
        services.AddScoped<LDKRapidGossipSyncer>();
        // services.AddScoped<IScopedHostedService>(provider =>
        //     provider.GetRequiredService<LDKSpendableOutputEventHandler>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKChannelSync>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKBackgroundProcessor>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKPeerHandler>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKAnnouncementBroadcaster>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<BTCPayPaymentsNotifier>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKPendingHTLCsForwardableEventHandler>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKRapidGossipSyncer>());

        services.AddScoped<OutputSweeper>(provider =>
        {
            var onchainWalletManager = provider.GetRequiredService<OnChainWalletManager>();
            var resp = onchainWalletManager.GetBestBlock().ConfigureAwait(false).GetAwaiter().GetResult();
            var hash = uint256.Parse(resp.BlockHash).ToBytes();
            var bestBlock = BestBlock.of(hash, (int) resp.BlockHeight);
            return OutputSweeper.of(bestBlock,
                provider.GetRequiredService<BroadcasterInterface>(),
                provider.GetRequiredService<FeeEstimator>(),
                Option_FilterZ.some(provider.GetRequiredService<Filter>()),
                provider.GetRequiredService<OutputSpender>(),
                provider.GetRequiredService<ChangeDestinationSource>(),
                provider.GetRequiredService<KVStore>(),
                provider.GetRequiredService<Logger>());
        });

        services.AddScoped<LDKLogger>();
        services.AddScoped<ChainParameters>(provider =>
        {
            var onchainWalletManager = provider.GetRequiredService<OnChainWalletManager>();
            var resp = onchainWalletManager.GetBestBlock().ConfigureAwait(false).GetAwaiter().GetResult();
            var hash = uint256.Parse(resp.BlockHash).ToBytes();

            var bestBlock = BestBlock.of(hash, (int) resp.BlockHeight);
            return ChainParameters.of(provider.GetRequiredService<Network>().GetLdkNetwork(), bestBlock);
        });
        services.AddScoped<Score>(provider => provider.GetRequiredService<ProbabilisticScorer>().as_Score());
        services.AddScoped<MultiThreadedLockableScore>(provider =>
            MultiThreadedLockableScore.of(provider.GetRequiredService<Score>()));
        services.AddScoped<LockableScore>(provider =>
            provider.GetRequiredService<MultiThreadedLockableScore>().as_LockableScore());
        services.AddScoped<WriteableScore>(provider =>
            provider.GetRequiredService<MultiThreadedLockableScore>().as_WriteableScore());

        services.AddScoped<LDKWalletLogger>();
        services.AddScoped<Logger>(provider => Logger.new_impl(provider.GetRequiredService<LDKWalletLogger>()));


        services.AddScoped<LDKEntropySource>();
        services.AddScoped<EntropySource>(provider =>
            EntropySource.new_impl(provider.GetRequiredService<LDKEntropySource>()));
        services.AddScoped<ProbabilisticScoringDecayParameters>(provider =>
            ProbabilisticScoringDecayParameters.with_default());
        services.AddScoped<ProbabilisticScorer>(provider =>
        {
            var configProvider = provider.GetRequiredService<IConfigProvider>();
            var bytes = configProvider.Get<byte[]>("Score").ConfigureAwait(false).GetAwaiter().GetResult();
            var logger = provider.GetRequiredService<Logger>();
            if (bytes is not null)
            {
                var result = ProbabilisticScorer.read(bytes,
                    provider.GetRequiredService<ProbabilisticScoringDecayParameters>(),
                    provider.GetRequiredService<NetworkGraph>(), logger);
                if (result is Result_ProbabilisticScorerDecodeErrorZ.Result_ProbabilisticScorerDecodeErrorZ_OK ok)
                    return ok.res;
            }

            return ProbabilisticScorer.of(ProbabilisticScoringDecayParameters.with_default(),
                provider.GetRequiredService<NetworkGraph>(), logger);
        });

        services.AddScoped<NetworkGraph>(provider =>
        {
            var configProvider = provider.GetRequiredService<IConfigProvider>();
            var bytes = configProvider.Get<byte[]>("NetworkGraph").ConfigureAwait(false).GetAwaiter().GetResult();
            if (bytes is not null)
            {
                var result = NetworkGraph.read(bytes, provider.GetRequiredService<Logger>());
                if (result is Result_NetworkGraphDecodeErrorZ.Result_NetworkGraphDecodeErrorZ_OK ok)
                    return ok.res;
            }

            return NetworkGraph.of(provider.GetRequiredService<Network>().GetLdkNetwork(),
                provider.GetRequiredService<Logger>());
        });
        services.AddScoped<DefaultRouter>(provider => DefaultRouter.of(provider.GetRequiredService<NetworkGraph>(),
            provider.GetRequiredService<Logger>(), provider.GetRequiredService<EntropySource>(),
            provider.GetRequiredService<LockableScore>(),
            ProbabilisticScoringFeeParameters.with_default()));
        services.AddScoped<Router>(provider => provider.GetRequiredService<DefaultRouter>().as_Router());

        
        services.AddScoped<VoltageFlow2Jit>();
        services.AddScoped<OlympusFlow2Jit>();
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<VoltageFlow2Jit>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<OlympusFlow2Jit>());
        services.AddScoped<IJITService, VoltageFlow2Jit>(provider => provider.GetRequiredService<VoltageFlow2Jit>());
        services.AddScoped<IJITService, OlympusFlow2Jit>(provider => provider.GetRequiredService<OlympusFlow2Jit>());
        
        return services;
    }

    public static IServiceCollection AddLDKEventHandler<T>(this IServiceCollection services)
        where T : class, ILDKEventHandler
    {
        services.TryAddScoped<T>();
        services.AddScoped<ILDKEventHandler>(provider => provider.GetRequiredService<T>());
        return services;
    }


    public static org.ldk.enums.Network GetLdkNetwork(this Network network)
    {
        return network.ChainName switch
        {
            { } cn when cn == ChainName.Mainnet => org.ldk.enums.Network.LDKNetwork_Bitcoin,
            { } cn when cn == ChainName.Testnet => org.ldk.enums.Network.LDKNetwork_Testnet,
            { } cn when cn == ChainName.Regtest => org.ldk.enums.Network.LDKNetwork_Regtest,
            _ => throw new NotSupportedException()
        };
    }

    public static org.ldk.enums.Currency GetLdkCurrency(this Network network)
    {
        return network.ChainName switch
        {
            { } cn when cn == ChainName.Mainnet => org.ldk.enums.Currency.LDKCurrency_Bitcoin,
            { } cn when cn == ChainName.Testnet => org.ldk.enums.Currency.LDKCurrency_BitcoinTestnet,
            { } cn when cn == ChainName.Regtest => org.ldk.enums.Currency.LDKCurrency_Regtest,
            _ => throw new NotSupportedException()
        };
    }


    // public static Logger GetGlobalLDKLogger(this IServiceProvider serviceProvider)
    // {
    //     return serviceProvider.GetRequiredKeyedService<Logger>(nameof(LDKLogger));
    // }

    public static NBitcoin.Coin Coin(this Input input)
    {
        return new NBitcoin.Coin(input.get_outpoint().Outpoint(), input.get_previous_utxo().TxOut());
    }

    public static TxOut TxOut(this org.ldk.structs.TxOut txOut)
    {
        return new TxOut(Money.Satoshis(txOut.value), Script.FromBytesUnsafe(txOut.script_pubkey));
    }

    public static NBitcoin.OutPoint Outpoint(this org.ldk.structs.OutPoint outPoint)
    {
        return new NBitcoin.OutPoint(new uint256(outPoint.get_txid()), outPoint.get_index());
    }

    public static org.ldk.structs.OutPoint Outpoint(this NBitcoin.OutPoint outPoint)
    {
        return org.ldk.structs.OutPoint.of(outPoint.Hash.ToBytes(), (short) outPoint.N);
    }

    public static org.ldk.structs.TxOut TxOut(this TxOut txOut)
    {
        return new org.ldk.structs.TxOut(txOut.Value.Satoshi, txOut.ScriptPubKey.ToBytes());
    }

    public static string GetReason(this Event.Event_ChannelClosed evt)
    {
        var reason = evt.reason.GetType().Name;
        switch (evt.reason)
        {

            case ClosureReason.ClosureReason_CounterpartyForceClosed closureReasonCounterpartyForceClosed:
                reason += " with msg from peer: " +closureReasonCounterpartyForceClosed.peer_msg.get_a();
                break;
            case ClosureReason.ClosureReason_ProcessingError closureReasonProcessingError:
                reason += " " + closureReasonProcessingError.err;
                break;
        }
        return reason;
    }
    

    public static byte[]? GetPreimage(this PaymentPurpose purpose, out byte[]? secret)
    {
        switch (purpose)
        {
            case PaymentPurpose.PaymentPurpose_Bolt11InvoicePayment paymentPurposeInvoicePayment:
                secret = paymentPurposeInvoicePayment.payment_secret;
                if (paymentPurposeInvoicePayment.payment_preimage is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some
                    some)
                    return some.some;
                return null;
            case PaymentPurpose.PaymentPurpose_Bolt12OfferPayment paymentPurposeInvoicePayment:
                secret = paymentPurposeInvoicePayment.payment_secret;
                if (paymentPurposeInvoicePayment.payment_preimage is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some
                    somex)
                    return somex.some;
                return null;
            case PaymentPurpose.PaymentPurpose_Bolt12RefundPayment paymentPurposeBolt12RefundPayment:
                secret = paymentPurposeBolt12RefundPayment.payment_secret;
                if (paymentPurposeBolt12RefundPayment.payment_preimage is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some
                    somey)
                    return somey.some;
                return null;
            case PaymentPurpose.PaymentPurpose_SpontaneousPayment paymentPurposeSpontaneousPayment:
                secret = null;
                return paymentPurposeSpontaneousPayment.spontaneous_payment;
            default:
                throw new ArgumentOutOfRangeException(nameof(purpose));
        }
    }
}