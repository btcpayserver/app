using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using NBitcoin;
using org.ldk.enums;
using org.ldk.structs;
using Coin = NBitcoin.Coin;
using Network = NBitcoin.Network;
using OutPoint = org.ldk.structs.OutPoint;
using Script = NBitcoin.Script;

namespace nldksample.LDK;

public class LDKPersistInterface : PersistInterface
{
    private readonly Network _network;
    private readonly CurrentWalletService _currentWalletService;

    public LDKPersistInterface(CurrentWalletService currentWalletService,
        Network network)
    {
        _network = network;
        _currentWalletService = currentWalletService;
    }

    public ChannelMonitorUpdateStatus persist_new_channel(OutPoint channel_id, ChannelMonitor data,
        MonitorUpdateId update_id)
    {
        //TODO: store update id  so that we can do this async
        var outs = data.get_outputs_to_watch();
        var coins = new List<Coin>();
        foreach (var output in outs)
        {
            var txId = new uint256(output.get_a());
            coins.AddRange(output.get_b().Select(zz =>
                new Coin(txId, (uint) zz.get_a(), Money.Zero, Script.FromBytesUnsafe(zz.get_b()))));
        }

        try
        {
            var scripts = coins.Select(coin => coin.ScriptPubKey.GetDestinationAddress(_network).ToString()).ToArray();
            _currentWalletService.TrackScripts(scripts).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_UnrecoverableError;
        }


        _walletService.TrackCoins(_walletId, coins.ToArray()).GetAwaiter().GetResult();

        _currentWalletService.AddOrUpdateChannel(new Channel()
        {
            Setting = new Setting() {WalletId = _walletId},
            FundingTransactionHash = new uint256(channel_id.get_txid()).ToString(),
            FundingTransactionOutputIndex = channel_id.get_index(),
            Data = data.write(),
        }).GetAwaiter().GetResult();

        return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
    }


    public ChannelMonitorUpdateStatus update_persisted_channel(OutPoint channel_id, ChannelMonitorUpdate update,
        ChannelMonitor data, MonitorUpdateId update_id)
    {
        _walletService.AddOrUpdateChannel(new Channel()
        {
            WalletId = _walletId, FundingTransactionHash = new uint256(channel_id.get_txid()).ToString(),
            FundingTransactionOutputIndex = channel_id.get_index(), Data = data.write()
        }).GetAwaiter().GetResult();

        return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
    }
}