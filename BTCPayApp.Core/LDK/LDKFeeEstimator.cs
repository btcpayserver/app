using BTCPayApp.Core.Wallet;
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

    public int get_est_sat_per_1000_weight(ConfirmationTarget confirmationTarget)
    {
        // https://docs.rs/lightning/latest/lightning/chain/chaininterface/enum.ConfirmationTarget.html
        var targetBlocks = confirmationTarget switch
        {
            ConfirmationTarget.LDKConfirmationTarget_MaximumFeeEstimate => 1,
            ConfirmationTarget.LDKConfirmationTarget_UrgentOnChainSweep => 32,
            ConfirmationTarget.LDKConfirmationTarget_MinAllowedAnchorChannelRemoteFee => 12,
            ConfirmationTarget.LDKConfirmationTarget_MinAllowedNonAnchorChannelRemoteFee => 4,
            ConfirmationTarget.LDKConfirmationTarget_AnchorChannelFee => 12,
            ConfirmationTarget.LDKConfirmationTarget_NonAnchorChannelFee => 2,
            ConfirmationTarget.LDKConfirmationTarget_ChannelCloseMinimum => 64,
            ConfirmationTarget.LDKConfirmationTarget_OutputSpendingFee => 144,
            _ => throw new ArgumentOutOfRangeException(nameof(confirmationTarget), confirmationTarget, null)
        };

        if (_network == Network.TestNet && targetBlocks >= 12)
            targetBlocks = 144;

        var feeRate = _onChainWalletManager.GetFeeRate(targetBlocks, confirmationTarget.ToString()).ConfigureAwait(false).GetAwaiter().GetResult();
        return (int)feeRate.FeePerK.Satoshi;
    }
}
