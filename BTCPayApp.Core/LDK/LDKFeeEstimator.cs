using BTCPayApp.Core.Attempt2;
using NBitcoin;
using org.ldk.enums;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKFeeEstimator : FeeEstimatorInterface
{
    private readonly OnChainWalletManager _onChainWalletManager;

    public LDKFeeEstimator( OnChainWalletManager onChainWalletManager)
    {
        _onChainWalletManager = onChainWalletManager;
    }

    public int get_est_sat_per_1000_weight(ConfirmationTarget confirmation_target)
    {
        var targetBlocks = confirmation_target switch
        {
            ConfirmationTarget.LDKConfirmationTarget_OnChainSweep => 30, // High priority (10-50 blocks)
            // ConfirmationTarget
            //         .LDKConfirmationTarget_MaxAllowedNonAnchorChannelRemoteFee =>
            //     20, // Moderate to high priority (small multiple of high-priority estimate)
            ConfirmationTarget
                    .LDKConfirmationTarget_MinAllowedAnchorChannelRemoteFee =>
                12, // Moderate priority (long-term mempool minimum or medium-priority)
            ConfirmationTarget
                    .LDKConfirmationTarget_MinAllowedNonAnchorChannelRemoteFee =>
                12, // Moderate priority (medium-priority feerate)
            ConfirmationTarget.LDKConfirmationTarget_AnchorChannelFee => 6, // Lower priority (can be bumped later)
            ConfirmationTarget
                .LDKConfirmationTarget_NonAnchorChannelFee => 20, // Moderate to high priority (high-priority feerate)
            ConfirmationTarget.LDKConfirmationTarget_ChannelCloseMinimum => 144, // Within a day or so (144-250 blocks)
            _ => throw new ArgumentOutOfRangeException(nameof(confirmation_target), confirmation_target, null)
        };
        return (int) _onChainWalletManager.GetFeeRate(targetBlocks).ConfigureAwait(false).GetAwaiter().GetResult().FeePerK.Satoshi;
    }
}