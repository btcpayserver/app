﻿using BTCPayApp.Core.Wallet;
using NBitcoin;
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

    public int get_est_sat_per_1000_weight(ConfirmationTarget confirmation_target)
    {
        var targetBlocks = confirmation_target switch
        {
            ConfirmationTarget.LDKConfirmationTarget_MaximumFeeEstimate => 50,
            ConfirmationTarget.LDKConfirmationTarget_UrgentOnChainSweep => 30, // High priority (10-50 blocks)
            ConfirmationTarget.LDKConfirmationTarget_MinAllowedAnchorChannelRemoteFee => 12, // Moderate priority (long-term mempool minimum or medium-priority)
            ConfirmationTarget.LDKConfirmationTarget_MinAllowedNonAnchorChannelRemoteFee => 12, // Moderate priority (medium-priority feerate)
            ConfirmationTarget.LDKConfirmationTarget_AnchorChannelFee => 6, // Lower priority (can be bumped later)
            ConfirmationTarget.LDKConfirmationTarget_NonAnchorChannelFee => 20, // Moderate to high priority (high-priority feerate)
            ConfirmationTarget.LDKConfirmationTarget_ChannelCloseMinimum => 144, // Within a day or so (144-250 blocks)
            ConfirmationTarget.LDKConfirmationTarget_OutputSpendingFee => 144,
            _ => throw new ArgumentOutOfRangeException(nameof(confirmation_target), confirmation_target, null)
        };

        if (_network == Network.TestNet  && targetBlocks >= 12)
                targetBlocks = 144;

        return (int) _onChainWalletManager.GetFeeRate(targetBlocks).ConfigureAwait(false).GetAwaiter().GetResult().FeePerK.Satoshi;
    }
}
