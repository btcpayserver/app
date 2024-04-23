using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using NBitcoin;
using NLDK;
using org.ldk.structs;
using Script = NBitcoin.Script;
using Transaction = NBitcoin.Transaction;
using TxOut = NBitcoin.TxOut;

namespace nldksample.LDK;

public class LDKFundingGenerationReadyEventHandler: ILDKEventHandler<Event.Event_FundingGenerationReady>
{
    private readonly LDKFeeEstimator _feeEstimator;
    private readonly CurrentWalletService _currentWalletService;
    private readonly ChannelManager _channelManager;
    private readonly WalletService _walletService;

    public record FundingTransactionGeneratedEvent(Event.Event_FundingGenerationReady evt, Transaction tx);
    public event EventHandler<FundingTransactionGeneratedEvent>? FundingTransactionGenerated;

    public LDKFundingGenerationReadyEventHandler(LDKFeeEstimator feeEstimator, CurrentWalletService currentWalletService, ChannelManager channelManager, WalletService walletService)
    {
        _feeEstimator = feeEstimator;
        _currentWalletService = currentWalletService;
        _channelManager = channelManager;
        _walletService = walletService;
    }
    public async Task Handle(Event.Event_FundingGenerationReady eventFundingGenerationReady)
    {
        var feeRate = await _feeEstimator.GetFeeRate();
        var txOuts = new List<TxOut>()
        {
            new(Money.Satoshis(eventFundingGenerationReady.channel_value_satoshis),
                Script.FromBytesUnsafe(eventFundingGenerationReady.output_script))
        };
        var tx = await _walletService.CreateTransaction(_currentWalletService.CurrentWallet, txOuts, feeRate);
        if (tx is null)
        {
            _channelManager.close_channel(eventFundingGenerationReady.temporary_channel_id, eventFundingGenerationReady.counterparty_node_id);
        }
        else
        {
            var result =   _channelManager.funding_transaction_generated(eventFundingGenerationReady.temporary_channel_id,
                eventFundingGenerationReady.counterparty_node_id, tx.Value.Tx.ToBytes());
            if (result.is_ok())
            {
                FundingTransactionGenerated?.Invoke(this, new FundingTransactionGeneratedEvent(eventFundingGenerationReady, tx.Value.Tx));
            }
        }
    }
}