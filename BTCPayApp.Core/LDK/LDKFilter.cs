using System.Text.Json.Serialization;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.JsonConverters;
using NBitcoin;
using org.ldk.structs;
using OutPoint = NBitcoin.OutPoint;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKFilter : FilterInterface
{
    private readonly LDKNode _ldkNode;
    private readonly IConfigProvider _configProvider;

    public LDKFilter(LDKNode ldkNode, IConfigProvider configProvider)
    {
        _ldkNode = ldkNode;
        _configProvider = configProvider;
    }

    public void register_tx(byte[] txid, byte[] script_pubkey)
    {
        var script = Script.FromBytesUnsafe(script_pubkey);
        Track(script);
    }

    public void register_output(WatchedOutput output)
    {
        var script = Script.FromBytesUnsafe(output.get_script_pubkey());

        AddOrUpdateWatchedOutput(new LDKWatchedOutput(output)).GetAwaiter().GetResult();
        Track(script);
    }

    public async Task<List<LDKWatchedOutput>> GetWatchedOutputs()
    {
        return (await _configProvider.Get<List<LDKWatchedOutput>>("ln:watchedOutputs"))?? new();
    }

    private SemaphoreSlim _semaphore = new(1, 1);

    private async Task AddOrUpdateWatchedOutput(LDKWatchedOutput output)
    {
        try
        {
            _ = _semaphore.WaitAsync();
            var watchedOutputs = await GetWatchedOutputs();
            var existing = watchedOutputs.FirstOrDefault(w => w.Outpoint == output.Outpoint);
            if (existing != null)
            {
                watchedOutputs.Remove(existing);
            }

            watchedOutputs.Add(output);
            await _configProvider.Set("ln:watchedOutputs", watchedOutputs, true);
        }
        finally
        {
            _semaphore.Release();
        }
    }


    private void Track(Script script)
    {
        _ = _ldkNode.TrackScripts([script]);
    }

    public async Task OutputsSpent(List<LDKWatchedOutput> spentWatchedOutputs)
    {
        var watchedOutputs = await GetWatchedOutputs();
        watchedOutputs.RemoveAll(w => spentWatchedOutputs.Any(s => s.Outpoint == w.Outpoint));
        await _configProvider.Set("ln:watchedOutputs", watchedOutputs, true);
    }
}

public class LDKWatchedOutput
{
    [JsonConverter(typeof(ScriptJsonConverter))]
    public Script Script { get; set; }

    [JsonConverter(typeof(UInt256JsonConverter))]
    public uint256? BlockHash { get; set; }

    [JsonConverter(typeof(OutPointJsonConverter))]
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