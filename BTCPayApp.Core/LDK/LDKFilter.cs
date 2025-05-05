using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using org.ldk.structs;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKFilter(LDKNode ldkNode, ConfigProvider configProvider) : FilterInterface
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

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
        return await GetWatchedOutputs(configProvider);
    }

    public static async Task<List<LDKWatchedOutput>> GetWatchedOutputs(ConfigProvider configProvider)
    {
        return await configProvider.Get<List<LDKWatchedOutput>?>("ln:watchedOutputs") ?? [];
    }

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
            await configProvider.Set("ln:watchedOutputs", watchedOutputs, true);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void Track(Script script)
    {
        _ = ldkNode.TrackScripts([script]);
    }

    public async Task OutputsSpent(List<LDKWatchedOutput> spentWatchedOutputs)
    {
        var watchedOutputs = await GetWatchedOutputs();
        watchedOutputs.RemoveAll(w => spentWatchedOutputs.Any(s => s.Outpoint == w.Outpoint));
        await configProvider.Set("ln:watchedOutputs", watchedOutputs, true);
    }
}
