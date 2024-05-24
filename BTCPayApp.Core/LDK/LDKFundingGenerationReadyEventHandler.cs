using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using NBitcoin;
using org.ldk.structs;
using Script = NBitcoin.Script;
using Transaction = NBitcoin.Transaction;
using TxOut = NBitcoin.TxOut;

namespace BTCPayApp.Core.LDK;

public class LDKFundingGenerationReadyEventHandler: ILDKEventHandler<Event.Event_FundingGenerationReady>
{
    private readonly OnChainWalletManager _onChainWalletManager;
    private readonly ChannelManager _channelManager;

    public record FundingTransactionGeneratedEvent(Event.Event_FundingGenerationReady evt, Transaction tx);
    public event AsyncEventHandler<FundingTransactionGeneratedEvent>? FundingTransactionGenerated;

    public LDKFundingGenerationReadyEventHandler( OnChainWalletManager onChainWalletManager, ChannelManager channelManager)
    {
        _onChainWalletManager = onChainWalletManager;
        _channelManager = channelManager;
    }
    public async Task Handle(Event.Event_FundingGenerationReady eventFundingGenerationReady)
    {
        var feeRate = await _onChainWalletManager.GetFeeRate(1);
        var txOuts = new List<TxOut>()
        {
            new(Money.Satoshis(eventFundingGenerationReady.channel_value_satoshis),
                Script.FromBytesUnsafe(eventFundingGenerationReady.output_script))
        };
        try
        {

            var tx = await _onChainWalletManager.CreateTransaction(txOuts, feeRate);
            var result =   _channelManager.funding_transaction_generated(eventFundingGenerationReady.temporary_channel_id,
                eventFundingGenerationReady.counterparty_node_id, tx.Tx.ToBytes());
            if (result.is_ok())
            {
                await FundingTransactionGenerated?.Invoke(this, new FundingTransactionGeneratedEvent(eventFundingGenerationReady, tx.Tx))!;
            }
        }
        catch (Exception e)
        {
            _channelManager.close_channel(eventFundingGenerationReady.temporary_channel_id, eventFundingGenerationReady.counterparty_node_id);
        }
    }
}