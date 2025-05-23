using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;
using NodeInfo = BTCPayServer.Lightning.NodeInfo;

namespace BTCPayApp.Core.LDK;

public class LDKPeerHandler(
    PeerManager peerManager,
    LDKWalletLoggerFactory logger,
    ChannelManager channelManager,
    LDKNode node,
    BTCPayConnectionManager btcPayConnectionManager,
    BTCPayAppServerClient btcPayAppServerClient)
    : IScopedHostedService
{
    private readonly ILogger<LDKPeerHandler> _logger = logger.CreateLogger<LDKPeerHandler>();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _configTcs;
    private readonly ObservableConcurrentDictionary<string, LDKTcpDescriptor> _descriptors = new();
    private readonly ConcurrentDictionary<string, Task<LDKTcpDescriptor?>> _connectionTasks = new();
    public EndPoint? Endpoint { get; set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        node.ConfigUpdated += ConfigUpdated;
        _descriptors.CollectionChanged += DescriptorsOnCollectionChanged;
        _ = ListenForInboundConnections(_cts.Token);
        _ = ContinuouslyAttemptToConnectToPersistentPeers(_cts.Token);
        _ = PeriodicTicker(_cts.Token, 1000, peerManager.process_events);

        btcPayAppServerClient.OnServerNodeInfo += PeerBtcPayServerHost;
        if (!string.IsNullOrEmpty(btcPayConnectionManager.ReportedNodeInfo))
            _ = PeerBtcPayServerHost(null, btcPayConnectionManager.ReportedNodeInfo);

        return Task.CompletedTask;
    }

    private void DescriptorsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        node.PeersChanged();
    }

    private async Task PeerBtcPayServerHost(object? sender, string? e)
    {
        var nodeInfo = NodeInfo.Parse(e);
        var config = await node.GetConfig();
        if (config.Peers.ContainsKey(nodeInfo.NodeId.ToString())) return;

        var endpoint = new IPEndPoint(IPAddress.Parse(nodeInfo.Host), nodeInfo.Port);
        await node.Peer(nodeInfo.NodeId, new PeerInfo
        {
            Label = "BTCPay Server Node",
            Endpoint = endpoint,
            Persistent = true,
            Trusted = true
        });
    }

    private Task ConfigUpdated(object? sender, LightningConfig e)
    {
        _configTcs?.TrySetResult();
        return Task.CompletedTask;
    }

    private async Task ContinuouslyAttemptToConnectToPersistentPeers(CancellationToken ctsToken)
    {
        while (!ctsToken.IsCancellationRequested)
        {
            try
            {
                var connected = peerManager.list_peers().Select(p => Convert.ToHexString(p.get_counterparty_node_id()).ToLower());
                var chans = await node.GetChannels(ctsToken);
                var channels = chans?.Where(pair => pair.channelDetails is not null)
                    .Select(pair => pair.channelDetails!).ToList() ?? [];
                var channelPeers = channels
                    .Select(details => Convert.ToHexString(details.get_counterparty().get_node_id()).ToLower()).Distinct();
                var config = await node.GetConfig();
                var missingConnections = config.Peers
                    .Where(pair => pair.Value.Persistent || channelPeers.Contains(pair.Key)).Select(pair => pair.Key)
                    .Except(connected, StringComparer.InvariantCultureIgnoreCase).ToList();

                var tasks = new List<Task>();
                foreach (var persistentPeer in missingConnections)
                {
                    var kv = config.Peers[persistentPeer];
                    if (kv.Endpoint is not { } endpoint) continue;
                    var nodeId = new PubKey(persistentPeer);
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(ctsToken);
                    cts.CancelAfter(10000);
                    tasks.Add(ConnectAsync(nodeId, endpoint, cts.Token));
                }
                await Task.WhenAll(tasks);
            }
            catch (Exception e) when (e is { InnerException: SocketException })
            {
                _logger.LogError(e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Error while attempting to connect to persistent peers");
            }
            finally
            {
                await Task.Delay(5000, ctsToken);
            }
        }
    }

    private async Task PeriodicTicker(CancellationToken cancellationToken, int ms, Action action)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(ms, cancellationToken);
            action.Invoke();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync().WithCancellation(cancellationToken);

        await Task.Run(() =>
        {
            _logger.LogInformation("Stopping, disconnecting all peers");
            peerManager.disconnect_all_peers();

        }, cancellationToken);

        node.ConfigUpdated -= ConfigUpdated;
        btcPayAppServerClient.OnServerNodeInfo -= PeerBtcPayServerHost;
        _descriptors.CollectionChanged -= DescriptorsOnCollectionChanged;
    }

    private async Task ListenForInboundConnections(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var config = await node.GetConfig();
            if (!config.AcceptInboundConnection)
            {
                _configTcs ??= new TaskCompletionSource();
                await _configTcs.Task.WaitAsync(cancellationToken);
                _configTcs = null;
               continue;
            }

            using var listener = new TcpListener(new IPEndPoint(IPAddress.Any, 0));
            listener.Start();
            var ip = listener.LocalEndpoint;
            Endpoint = new IPEndPoint(IPAddress.Loopback, (int)ip.Port());
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = LDKTcpDescriptor.Inbound(peerManager,
                    await listener.AcceptTcpClientAsync(cancellationToken),
                    _logger, _descriptors);
                if (result is not null)
                {
                    _descriptors.TryAdd(result.Id, result);
                    peerManager.process_events();
                }
            }
        }
    }

    public async Task<LDKTcpDescriptor?> ConnectAsync(NodeInfo nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var remote = nodeInfo.Host + ":" + nodeInfo.Port;
        if (EndPointParser.TryParse(remote, 9735, out var endpoint))
            return await ConnectAsync(nodeInfo.NodeId, endpoint, cancellationToken);
        throw new ArgumentException($"Invalid endpoint: {remote}", nameof(nodeInfo));
    }

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey peerNodeId, PeerInfo peerInfo, CancellationToken cancellationToken = default)
    {
        if (peerInfo.Endpoint is not { } endpoint) return null;
        if (peerInfo.Label is not null)
            _logger.LogInformation("Attempting to connect to {NodeId} at {Endpoint} ({Label})", peerNodeId, endpoint.ToEndpointString(), peerInfo.Label);
        return await ConnectAsync(peerNodeId, endpoint, cancellationToken);
    }

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey theirNodeId, EndPoint remote, CancellationToken cancellationToken = default)
    {
        var nodeId = theirNodeId.ToString();
        //cache this task so that we don't have multiple attempts to connect to the same place
        if (_connectionTasks.TryGetValue(nodeId, out var task))
        {
            _logger.LogInformation("Already attempting to connect to {NodeId}", nodeId);
            return await task.WithCancellation(cancellationToken);
        }

        var tcs = new TaskCompletionSource<LDKTcpDescriptor?>();
        try
        {
            if (!_connectionTasks.TryAdd(nodeId, tcs.Task))
                return null;

            if (channelManager.get_our_node_id().SequenceEqual(theirNodeId.ToBytes()))
                return null;

            var ipEndpoint = remote.IPEndPoint();
            var client = new TcpClient();
            await client.ConnectAsync(ipEndpoint, cancellationToken);

            var result = LDKTcpDescriptor.Outbound(peerManager, client, _logger, theirNodeId, _descriptors);
            if (result is not null)
            {
                var needsUpdate = false;
                var config = await node.GetConfig();
                if (!config.Peers.TryGetValue(nodeId, out var peer))
                {
                    peer = new PeerInfo { Trusted = true };
                    needsUpdate = true;
                }
                if (!peer.Persistent)
                {
                    peer.Persistent = true;
                    needsUpdate = true;
                }
                if (peer.Endpoint?.ToEndpointString() != remote.ToString())
                {
                    peer.Endpoint = remote;
                    needsUpdate = true;
                }
                if (needsUpdate)
                    await node.Peer(theirNodeId, peer);

                _descriptors.TryAdd(result.Id, result);
                peerManager.process_events();
            }

            tcs.TrySetResult(result);
        }
        catch (SocketException e)
        {
            // wrap the original exception to report on the failing node URI
            tcs.TrySetException(new Exception($"Socket connection to {nodeId}@{remote} failed: {e.Message}", e));
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
        finally
        {
            _connectionTasks.TryRemove(nodeId, out _);
        }

        return await tcs.Task;
    }

    public async Task DisconnectAsync(PubKey id)
    {
       _logger.LogInformation("Disconnecting from {NodeId}", id);
       peerManager.disconnect_by_node_id(id.ToBytes());

       var config = await node.GetConfig();
       if (config.Peers.TryGetValue(id.ToString(), out var peer) && peer.Persistent)
       {
           peer.Persistent = false;
           await node.Peer(id, peer);
       }

       peerManager.process_events();
       _logger.LogInformation("Disconnected from {NodeId}", id);
    }
}
