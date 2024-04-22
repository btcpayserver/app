using System.Net;
using System.Net.Sockets;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json;
using NLDK;
using nldksample.LSP.Flow;
using org.ldk.enums;
using org.ldk.structs;
using Network = NBitcoin.Network;
using Script = NBitcoin.Script;
using SocketAddress = org.ldk.structs.SocketAddress;
using TxOut = NBitcoin.TxOut;

namespace nldksample.LDK;

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

        if(remote is null)
            return Option_SocketAddressZ.none();
        var ipe = ((IPEndPoint) socket.RemoteEndPoint);
        
        // return Option_SocketAddressZ.some(SocketAddress.tcp_ip_v4(ipe.Address.GetAddressBytes(), (short) ipe.Port));
        var socketAddress =SocketAddress.from_str(remote);
       return !socketAddress.is_ok() ? Option_SocketAddressZ.none() : Option_SocketAddressZ.some(((Result_SocketAddressSocketAddressParseErrorZ.Result_SocketAddressSocketAddressParseErrorZ_OK)socketAddress).res);
    }
    
    public static SocketAddress? Endpoint(this EndPoint endPoint)
    {
        return SocketAddress.from_str(endPoint.ToString()) switch
        {
            org.ldk.structs.Result_SocketAddressSocketAddressParseErrorZ.Result_SocketAddressSocketAddressParseErrorZ_OK ok => ok.res,
            _ => null
        };
    }
    
    public class LDKEntropySource:EntropySourceInterface
    {
        public byte[] get_secure_random_bytes()
        {
            return RandomUtils.GetBytes(32);
        }
    }

    public static IServiceCollection AddLDK(this IServiceCollection services)
    { 
        services.AddScoped<CurrentWalletService>();

        services.AddScoped<KeysManager>(provider => KeysManager.of(provider.GetRequiredService<CurrentWalletService>().Seed, DateTimeOffset.Now.ToUnixTimeSeconds(),
            RandomUtils.GetInt32()));
        services.AddScoped(provider => provider.GetRequiredService<KeysManager>().as_NodeSigner());
        services.AddScoped<LDKPersister>();
        services.AddScoped<Persister>(provider =>
            Persister.new_impl(provider.GetRequiredService<LDKPersister>()));
        services.AddScoped(provider => UserConfig.with_default());
        
        
        services.AddScoped(provider =>
        {
            var feeEstimator = provider.GetRequiredService<FeeEstimator>();
            var walletService = provider.GetRequiredService<WalletService>();
            var watch = provider.GetRequiredService<Watch>();
            var broadcasterInterface = provider.GetRequiredService<BroadcasterInterface>();
            var router = provider.GetRequiredService<Router>();
            var logger = provider.GetRequiredService<Logger>();
            var signerProvider = provider.GetRequiredService<SignerProvider>();
            var userConfig = provider.GetRequiredService<UserConfig>();
            var entropySource = provider.GetRequiredService<EntropySource>();
            var nodeSigner = provider.GetRequiredService<NodeSigner>();
            var chainParameters = provider.GetRequiredService<ChainParameters>();
            var currentWalletService = provider.GetRequiredService<CurrentWalletService>();
            var filter = provider.GetRequiredService<Filter>();
            var channelManagerSerialized = currentWalletService
                .GetSerializedChannelManager(entropySource, signerProvider).ConfigureAwait(false).GetAwaiter()
                .GetResult();
                
            if (channelManagerSerialized is not null)
            {
                return ChannelManagerHelper.Load(channelManagerSerialized.Value.channelMonitors, channelManagerSerialized.Value.serializedChannelManager, entropySource, signerProvider, nodeSigner,
                    feeEstimator, watch, broadcasterInterface, router, logger, userConfig, filter);
            }
            return  ChannelManager.of(feeEstimator, watch, broadcasterInterface, router, logger,entropySource ,nodeSigner, signerProvider,userConfig,chainParameters,
                (int) DateTimeOffset.Now.ToUnixTimeSeconds());
        });
        services.AddScoped(provider => provider.GetRequiredService<ChannelManager>().as_ChannelMessageHandler());
        services.AddScoped(provider => provider.GetRequiredService<ChannelManager>().as_OffersMessageHandler());
        services.AddScoped<LDKNode>();
        services.AddSingleton(provider => P2PGossipSync.of(provider.GetRequiredService<NetworkGraph>(), Option_UtxoLookupZ.none(), provider.GetGlobalLDKLogger()));
        services.AddSingleton(provider => GossipSync.p2_p(provider.GetRequiredService<P2PGossipSync>()));
        services.AddSingleton(provider => DefaultMessageRouter.of(provider.GetRequiredService<NetworkGraph>(),provider.GetRequiredService<EntropySource>()));
        services.AddSingleton(provider => provider.GetRequiredService<P2PGossipSync>().as_RoutingMessageHandler());
        services.AddSingleton(provider =>  provider.GetRequiredService<DefaultMessageRouter>().as_MessageRouter());
        services.AddSingleton(provider =>  IgnoringMessageHandler.of().as_CustomOnionMessageHandler());
        services.AddSingleton(provider =>  IgnoringMessageHandler.of().as_CustomMessageHandler());
        services.AddScoped<OnionMessenger>(provider => 
            OnionMessenger.of(
                provider.GetRequiredService<EntropySource>(), 
                provider.GetRequiredService<NodeSigner>(), 
                provider.GetRequiredService<Logger>(),
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
        services.AddLDKEventHandler<LDKOpenChannelRequestEventHandler>();
        services.AddLDKEventHandler<LDKPaymentEventsHandler>();
        services.AddLDKEventHandler<LDKPendingHTLCsForwardableEventHandler>();
        services.AddLDKEventHandler<LDKAnnouncementBroadcaster>();
        
        services.AddScoped<LDKEventHandler>();
        services.AddScoped<org.ldk.structs.EventHandler>(provider =>
            org.ldk.structs.EventHandler.new_impl(provider.GetRequiredService<LDKEventHandler>()));
        services.AddScoped<LDKFeeEstimator>();
        services.AddScoped<LDKWalletLoggerFactory>();
        services.AddScoped<FeeEstimator>(provider =>
            FeeEstimator.new_impl(provider.GetRequiredService<LDKFeeEstimator>()));
        services.AddScoped<LDKPersistInterface>();
        services.AddScoped<Persist>(provider => Persist.new_impl(provider.GetRequiredService<LDKPersistInterface>()));
        services.AddScoped<LDKSignerProvider>();
        services.AddScoped<SignerProvider>(provider =>
            SignerProvider.new_impl(provider.GetRequiredService<LDKSignerProvider>()));
        services.AddScoped<LDKFilter>();
        services.AddScoped<Filter>(provider => Filter.new_impl(provider.GetRequiredService<LDKFilter>()));
        services.AddScoped<ChainMonitor>(provider =>
            ChainMonitor.of(
                Option_FilterZ.some(provider.GetRequiredService<Filter>()),
                provider.GetRequiredService<BroadcasterInterface>(),
                provider.GetRequiredService<Logger>(),
                provider.GetRequiredService<FeeEstimator>(),
                provider.GetRequiredService<Persist>()
            ));
        services.AddScoped<Watch>(provider => provider.GetRequiredService<ChainMonitor>().as_Watch());
        services.AddScoped<Filter>(provider => Filter.new_impl(provider.GetRequiredService<LDKFilter>()));
        services.AddScoped<Confirm, Confirm>(provider => provider.GetRequiredService<ChannelManager>().as_Confirm());
        services.AddScoped<Confirm, Confirm>(provider => provider.GetRequiredService<ChainMonitor>().as_Confirm());
        services.AddScoped<LDKChannelSync>();
        services.AddScoped<LDKPeerHandler>();
        services.AddScoped<LDKBackgroundProcessor>();
        services.AddScoped<LDKAnnouncementBroadcaster>();
        services.AddScoped<PaymentsManager>();
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKChannelSync>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKBackgroundProcessor>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKPeerHandler>());
        services.AddScoped<IScopedHostedService>(provider => provider.GetRequiredService<LDKAnnouncementBroadcaster>());

        services.AddSingleton<LDKLogger>();
        services.AddSingleton<ChainParameters>(provider =>
        {
            var explorerClient = provider.GetRequiredService<ExplorerClient>();
            var info = explorerClient.RPCClient.GetBlockchainInfo();
            var bestBlock = BestBlock.of(info.BestBlockHash.ToBytes(), (int) info.Blocks);
            return ChainParameters.of(provider.GetRequiredService<Network>().GetLdkNetwork(), bestBlock);
        });
        services.AddSingleton<Score>(provider => provider.GetRequiredService<ProbabilisticScorer>().as_Score());
         services.AddSingleton<MultiThreadedLockableScore>(provider =>
            MultiThreadedLockableScore.of(provider.GetRequiredService<Score>()));
        services.AddSingleton<LockableScore>(provider =>
            provider.GetRequiredService<MultiThreadedLockableScore>().as_LockableScore());
        services.AddSingleton<WriteableScore>(provider =>
            provider.GetRequiredService<MultiThreadedLockableScore>().as_WriteableScore());

        services.AddKeyedSingleton<Logger>(nameof(LDKLogger), (provider, o) => Logger.new_impl(provider.GetRequiredService<LDKLogger>()));
        services.AddScoped<LDKWalletLogger>();
        services.AddScoped<Logger>(provider => Logger.new_impl(provider.GetRequiredService<LDKWalletLogger>()));
        services.AddSingleton<NetworkGraph>(provider =>
        {
            var nodeManager = provider.GetRequiredService<LDKNodeManager>();
            if (nodeManager.Data.TryGetValue("NetworkGraph", out var s))
            {
                var result =  NetworkGraph.read(s, provider.GetGlobalLDKLogger());                
                if(result is Result_NetworkGraphDecodeErrorZ.Result_NetworkGraphDecodeErrorZ_OK ok)
                    return ok.res;
            }
            return NetworkGraph.of(provider.GetRequiredService<Network>().GetLdkNetwork(),
                provider.GetGlobalLDKLogger());
        });
        
        
        services.AddSingleton<LDKEntropySource>();
        services.AddSingleton<EntropySource>(provider => EntropySource.new_impl(provider.GetRequiredService<LDKEntropySource>()));
        services.AddSingleton<ProbabilisticScoringDecayParameters>(provider => ProbabilisticScoringDecayParameters.with_default());
        services.AddSingleton<ProbabilisticScorer>(provider =>
        {
            var nodeManager = provider.GetRequiredService<LDKNodeManager>();
            var logger = provider.GetGlobalLDKLogger();
            if (nodeManager.Data.TryGetValue("Score", out var s))
            {
                var result =  ProbabilisticScorer.read(s, provider.GetRequiredService<ProbabilisticScoringDecayParameters>(), provider.GetRequiredService<NetworkGraph>(), logger);                
                if(result is Result_ProbabilisticScorerDecodeErrorZ.Result_ProbabilisticScorerDecodeErrorZ_OK ok)
                    return ok.res;
            }
            
            return ProbabilisticScorer.of(ProbabilisticScoringDecayParameters.with_default(),
                provider.GetRequiredService<NetworkGraph>(), logger);
        });
        services.AddSingleton<DefaultRouter>(provider => DefaultRouter.of(provider.GetRequiredService<NetworkGraph>(),
            provider.GetGlobalLDKLogger(),provider.GetRequiredService<EntropySource>(),
            provider.GetRequiredService<LockableScore>(),
            ProbabilisticScoringFeeParameters.with_default()));
        services.AddSingleton<Router>(provider => provider.GetRequiredService<DefaultRouter>().as_Router());
        services.AddSingleton<LDKNodeManager>();
        services.AddHostedService<LDKNodeManager>(provider => provider.GetRequiredService<LDKNodeManager>());

        return services;
    }

    public static IServiceCollection AddLDKEventHandler<T>(this IServiceCollection services) where T : class, ILDKEventHandler
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


    public static Logger GetGlobalLDKLogger(this IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredKeyedService<Logger>(nameof(LDKLogger));
    }

    public static async Task<GetBlockchainInfoResponse> GetBlockchainInfoAsyncEx(this RPCClient client,
        CancellationToken cancellationToken = default)
    {
        var result = await client.SendCommandAsync("getblockchaininfo", cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<GetBlockchainInfoResponse>(result.ResultString);
    }

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
    public static  org.ldk.structs.OutPoint Outpoint(this NBitcoin.OutPoint  outPoint)
    {
        return org.ldk.structs.OutPoint.of(outPoint.Hash.ToBytes(), (short) outPoint.N);
    }
    
    public static org.ldk.structs.TxOut TxOut(this TxOut txOut)
    {
        return new org.ldk.structs.TxOut(txOut.Value.Satoshi, txOut.ScriptPubKey.ToBytes());
    }


    public static byte[]? GetPreimage(this PaymentPurpose purpose, out byte[]? secret)
    {
        switch (purpose)
        {
            case PaymentPurpose.PaymentPurpose_InvoicePayment paymentPurposeInvoicePayment:
                secret = paymentPurposeInvoicePayment.payment_secret;
                if (paymentPurposeInvoicePayment.payment_preimage is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some
                    some)
                    return some.some;
                return null;
            case PaymentPurpose.PaymentPurpose_SpontaneousPayment paymentPurposeSpontaneousPayment:
                secret = null;
                return paymentPurposeSpontaneousPayment.spontaneous_payment;
            default:
                throw new ArgumentOutOfRangeException(nameof(purpose));
        }
    }
    
    
}