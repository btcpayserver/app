using BTCPayApp.Core.Wallet;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// Provides a bitcoin address from the main wallet when sweeping funds from closed channels 
/// </summary>
public class LDKChangeDestinationSource:ChangeDestinationSourceInterface
{
    private readonly LightningNodeManager _lightningNodeManager;

    public LDKChangeDestinationSource( LightningNodeManager lightningNodeManager)
    {
        _lightningNodeManager = lightningNodeManager;
    }
    public Result_CVec_u8ZNoneZ get_change_destination_script()
    {
        var s = _lightningNodeManager.Node.DeriveScript().ConfigureAwait(false).GetAwaiter().GetResult();
       return Result_CVec_u8ZNoneZ.ok(s.ToBytes());
    }
}