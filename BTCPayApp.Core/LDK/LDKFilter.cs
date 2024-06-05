using BTCPayApp.Core.Attempt2;
using org.ldk.structs;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKFilter : FilterInterface
{
    private readonly LDKNode _ldkNode;

    public LDKFilter(LDKNode ldkNode)
    {
        _ldkNode = ldkNode;
    }

    public void register_tx(byte[] txid, byte[] script_pubkey)
    {
        var script = Script.FromBytesUnsafe(script_pubkey);
        Track(script);
    }

    public void register_output(WatchedOutput output)
    {
        var script = Script.FromBytesUnsafe(output.get_script_pubkey());
        Track(script);
       
    }

    private void Track(Script script)
    {
        Task.Run(async () => await _ldkNode.TrackScripts(new[] {script}));
    }
}