﻿using AsyncKeyedLock;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using org.ldk.structs;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKFilter : FilterInterface
{
    private readonly LDKNode _ldkNode;
    private readonly ConfigProvider _configProvider;
    private readonly AsyncNonKeyedLocker _semaphore = new();

    public LDKFilter(LDKNode ldkNode, ConfigProvider configProvider)
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
        return await GetWatchedOutputs(_configProvider);
    }

    public static async Task<List<LDKWatchedOutput>> GetWatchedOutputs(ConfigProvider configProvider)
    {
        return await configProvider.Get<List<LDKWatchedOutput>?>("ln:watchedOutputs") ?? [];
    }

    private async Task AddOrUpdateWatchedOutput(LDKWatchedOutput output)
    {
        using var _ = await _semaphore.LockAsync();
        var watchedOutputs = await GetWatchedOutputs();
        var existing = watchedOutputs.FirstOrDefault(w => w.Outpoint == output.Outpoint);
        if (existing != null)
        {
            watchedOutputs.Remove(existing);
        }

        watchedOutputs.Add(output);
        await _configProvider.Set("ln:watchedOutputs", watchedOutputs, true);
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
