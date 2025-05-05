using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKRapidGossipSyncer(
    LDKNode ldkNode,
    RapidGossipSync rapidGossipSync,
    NetworkGraph networkGraph,
    IHttpClientFactory httpClientFactory,
    ILogger<LDKRapidGossipSyncer> logger)
    : IScopedHostedService
{
    private CancellationTokenSource? _cts;
    private TaskCompletionSource _configUpdated = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ldkNode.ConfigUpdated += OnConfigUpdated;
        _ = UpdateNetworkGraph();
        return Task.CompletedTask;
    }

    private Task OnConfigUpdated(object? sender, LightningConfig e)
    {
        _configUpdated.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task UpdateNetworkGraph()
    {
        while (_cts is not null && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                _configUpdated = new();
                var config = await ldkNode.GetConfig();
                if (config.RapidGossipSyncUrl is null)
                {
                    try
                    {
                        await _configUpdated.Task.WithCancellation(_cts.Token);
                        continue;
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }
                    // wait until config is updated or _cts is cancelled
                }

                var timestamp  = networkGraph.get_last_rapid_gossip_sync_timestamp() is Option_u32Z.Option_u32Z_Some some
                    ? some.some : 0;
                var uri = new Uri(config.RapidGossipSyncUrl, $"/snapshot/{timestamp}");
                var response = await httpClientFactory.CreateClient("rgs").GetAsync(uri, _cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogError("Failed to download snapshot from {uri}", uri);
                    continue;
                }

                var snapshot = await response.Content.ReadAsByteArrayAsync();
                var result =
                    rapidGossipSync.update_network_graph_no_std(snapshot,
                        Option_u64Z.some(DateTime.Now.ToUnixTimestamp()));
                if (result is Result_u32GraphSyncErrorZ.Result_u32GraphSyncErrorZ_Err err)
                {
                    switch (err.err)
                    {
                        case GraphSyncError.GraphSyncError_DecodeError graphSyncErrorDecodeError:
                            logger.LogError(
                                $"Failed to decode snapshot from {uri} with error {graphSyncErrorDecodeError.decode_error.GetType().Name}");
                            break;
                        case GraphSyncError.GraphSyncError_LightningError graphSyncErrorLightningError:
                            logger.LogError(
                                $"Failed to update network graph with error {graphSyncErrorLightningError.lightning_error.get_err()}");
                            // config = await _ldkNode.GetConfig();
                            // await _ldkNode.UpdateConfig(config);
                            continue;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }


                await Task.Delay(10000, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while updating network graph");
                await Task.Delay(10000, _cts.Token);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ldkNode.ConfigUpdated -= OnConfigUpdated;
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts?.Dispose();
        }
    }
}
