using BTCPayApp.Core;
using NBitcoin;
using org.ldk.enums;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKFeeEstimator : FeeEstimatorInterface
{
    private readonly BTCPayConnection _connection;

    public LDKFeeEstimator(BTCPayConnection connection)
    {
        _connection = connection;
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
        return (int) GetFeeRate(targetBlocks).ConfigureAwait(false).GetAwaiter().GetResult().FeePerK.Satoshi;
    }

    public async Task<FeeRate> GetFeeRate(int blockTarget = 3)
    {
        var result =  await _connection.HubProxy.GetFeeRate(blockTarget);
        return new FeeRate(result);
    }
}