using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.Attempt2;

public class LDKRapidGossipSyncer : IScopedHostedService
{
    private readonly LDKNode _ldkNode;
    private readonly RapidGossipSync _rapidGossipSync;
    private readonly NetworkGraph _networkGraph;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LDKRapidGossipSyncer> _logger;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource _configUpdated = new();

    public LDKRapidGossipSyncer(LDKNode ldkNode,
        RapidGossipSync rapidGossipSync,
        NetworkGraph networkGraph,
        IHttpClientFactory httpClientFactory, ILogger<LDKRapidGossipSyncer> logger)
    {
        _ldkNode = ldkNode;
        _rapidGossipSync = rapidGossipSync;
        _networkGraph = networkGraph;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ldkNode.ConfigUpdated += OnConfigUpdated;
        _ = UpdateNetworkGraph();
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
                var config = await _ldkNode.GetConfig();
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

                var timestamp  = _networkGraph.get_last_rapid_gossip_sync_timestamp() is Option_u32Z.Option_u32Z_Some some 
                    ? some.some : 0;
                var uri = new Uri(config.RapidGossipSyncUrl, $"/snapshot/{timestamp}");
                var response = await _httpClientFactory.CreateClient("rgs").GetAsync(uri, _cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to download snapshot from {uri}", uri);
                    continue;
                }
                
                var snapshot = await response.Content.ReadAsByteArrayAsync();
                var result =
                    _rapidGossipSync.update_network_graph_no_std(snapshot,
                        Option_u64Z.some(DateTime.Now.ToUnixTimestamp()));
                if (result is Result_u32GraphSyncErrorZ.Result_u32GraphSyncErrorZ_Err err)
                {
                    switch (err.err)
                    {
                        case GraphSyncError.GraphSyncError_DecodeError graphSyncErrorDecodeError:
                            _logger.LogError(
                                $"Failed to decode snapshot from {uri} with error {graphSyncErrorDecodeError.decode_error.GetType().Name}");
                            break;
                        case GraphSyncError.GraphSyncError_LightningError graphSyncErrorLightningError:
                            _logger.LogError(
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
                _logger.LogError(e, "Error while updating network graph");
                await Task.Delay(10000, _cts.Token);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ldkNode.ConfigUpdated -= OnConfigUpdated;
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts?.Dispose();
        }
    }
}