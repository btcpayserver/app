using BTCPayApp.Core.Wallet;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// Provides a bitcoin address from the main wallet when sweeping funds from closed channels
/// </summary>
public class LDKChangeDestinationSource(LightningNodeManager lightningNodeManager) : ChangeDestinationSourceInterface
{
    public Result_CVec_u8ZNoneZ get_change_destination_script()
    {
        var s = lightningNodeManager.Node.DeriveScript().ConfigureAwait(false).GetAwaiter().GetResult();
        return Result_CVec_u8ZNoneZ.ok(s.ToBytes());
    }
}
