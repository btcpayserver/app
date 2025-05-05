using NBitcoin;
using org.ldk.structs;
using org.ldk.util;
using UInt128 = org.ldk.util.UInt128;

namespace BTCPayApp.Core.LDK;

public class LDKSignerProvider(KeysManager innerSigner, LDKNode ldkNode) : SignerProviderInterface
{
    private readonly SignerProvider _innerSigner = innerSigner.as_SignerProvider();

    public byte[] generate_channel_keys_id(bool inbound, long channel_value_satoshis, UInt128 user_channel_id)
    {
        return _innerSigner.generate_channel_keys_id(inbound, channel_value_satoshis, user_channel_id);
    }

    public EcdsaChannelSigner derive_channel_signer(long channel_value_satoshis, byte[] channel_keys_id)
    {
        return _innerSigner.derive_channel_signer(channel_value_satoshis, channel_keys_id);
    }

    public Result_EcdsaChannelSignerDecodeErrorZ read_chan_signer(byte[] reader)
    {
        return _innerSigner.read_chan_signer(reader);
    }

    public Result_CVec_u8ZNoneZ get_destination_script(byte[] channel_keys_id)
    {
        var script = ldkNode.DeriveScript().GetAwaiter().GetResult();
        return Result_CVec_u8ZNoneZ.ok(script.ToBytes());
    }

    public Result_ShutdownScriptNoneZ get_shutdown_scriptpubkey()
    {
        var script = ldkNode.DeriveScript().GetAwaiter().GetResult();

        if (!script.IsScriptType(ScriptType.Witness))
            throw new NotSupportedException("Generated a non witness script.");

        var witnessParams = PayToWitTemplate.Instance.ExtractScriptPubKeyParameters2(script);
        var result = ShutdownScript.new_witness_program(new WitnessProgram(witnessParams.Program, new WitnessVersion((byte) witnessParams.Version)));
        return result is Result_ShutdownScriptInvalidShutdownScriptZ.Result_ShutdownScriptInvalidShutdownScriptZ_OK ok
            ? Result_ShutdownScriptNoneZ.ok(ok.res)
            : Result_ShutdownScriptNoneZ.err();
    }
}
