using BTCPayApp.Core.Wallet;
using org.ldk.enums;
using org.ldk.structs;
using Network = NBitcoin.Network;

namespace BTCPayApp.Core.LDK;

public class LDKFeeEstimator : FeeEstimatorInterface
{
    private readonly OnChainWalletManager _onChainWalletManager;
    private readonly Network _network;

    public LDKFeeEstimator( OnChainWalletManager onChainWalletManager, Network network)
    {
        _onChainWalletManager = onChainWalletManager;
        _network = network;
    }

    public int get_est_sat_per_1000_weight(ConfirmationTarget confirmationTarget)
    {
        // Try fixing this to 1sat/vByte, see also
        // https://github.com/lightningdevkit/rust-lightning/blob/master/lightning/src/chain/chaininterface.rs#L183
        // https://github.com/MutinyWallet/mutiny-node/blob/master/mutiny-core/src/fees.rs#L193
        if (confirmationTarget == ConfirmationTarget.LDKConfirmationTarget_MinAllowedAnchorChannelRemoteFee)
            return 253;

        // https://docs.rs/lightning/latest/lightning/chain/chaininterface/enum.ConfirmationTarget.html
        // https://github.com/lightningdevkit/ldk-node/blob/main/src/fee_estimator.rs#L87
        var targetBlocks = confirmationTarget switch
        {
            ConfirmationTarget.LDKConfirmationTarget_MaximumFeeEstimate => 1,
            ConfirmationTarget.LDKConfirmationTarget_UrgentOnChainSweep => 6,
            //ConfirmationTarget.LDKConfirmationTarget_MinAllowedAnchorChannelRemoteFee => 1008,
            ConfirmationTarget.LDKConfirmationTarget_MinAllowedNonAnchorChannelRemoteFee => 144,
            ConfirmationTarget.LDKConfirmationTarget_AnchorChannelFee => 1008,
            ConfirmationTarget.LDKConfirmationTarget_NonAnchorChannelFee => 12,
            ConfirmationTarget.LDKConfirmationTarget_ChannelCloseMinimum => 144,
            ConfirmationTarget.LDKConfirmationTarget_OutputSpendingFee => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(confirmationTarget), confirmationTarget, null)
        };

        if (_network == Network.TestNet && targetBlocks >= 12)
            targetBlocks = 144;

        var feeRate = _onChainWalletManager.GetFeeRate(targetBlocks, confirmationTarget.ToString()).ConfigureAwait(false).GetAwaiter().GetResult();
        return (int)feeRate.FeePerK.Satoshi;
    }
}
