using System.Text.Json.Serialization;
using BTCPayApp.Core.JsonConverters;
using NBitcoin;
using org.ldk.structs;
using OutPoint = NBitcoin.OutPoint;

namespace BTCPayApp.Core.LDK;

public class LDKWatchedOutput
{
    [JsonConverter(typeof(ScriptJsonConverter))]
    public Script Script { get; set; }

    [JsonConverter(typeof(UInt256JsonConverter))]
    public uint256? BlockHash { get; set; }

    [JsonConverter(typeof(BitcoinSerializableJsonConverterFactory))]
    public OutPoint Outpoint { get; set; }

    public LDKWatchedOutput()
    {
    }

    public LDKWatchedOutput(WatchedOutput watchedOutput)
    {
        Script = Script.FromBytesUnsafe(watchedOutput.get_script_pubkey());
        BlockHash = watchedOutput.get_block_hash() is Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some some
            ? new uint256(some.some)
            : null;
        Outpoint = watchedOutput.get_outpoint().Outpoint();
    }
}