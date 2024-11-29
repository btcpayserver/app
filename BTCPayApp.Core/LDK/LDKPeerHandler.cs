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

public class LDKPeerHandler : IScopedHostedService
{
    private readonly ILogger<LDKPeerHandler> _logger;
    private readonly PeerManager _peerManager;
    private readonly ChannelManager _channelManager;
    private readonly LDKNode _node;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly BTCPayAppServerClient _btcPayAppServerClient;
    private CancellationTokenSource? _cts;


    private readonly ObservableConcurrentDictionary<string, LDKTcpDescriptor> _descriptors = new();

    public LDKPeerHandler(PeerManager peerManager, LDKWalletLoggerFactory logger, ChannelManager channelManager,
        LDKNode node,
        BTCPayConnectionManager btcPayConnectionManager, BTCPayAppServerClient btcPayAppServerClient)
    {
        _peerManager = peerManager;
        _channelManager = channelManager;
        _node = node;
        _btcPayConnectionManager = btcPayConnectionManager;
        _btcPayAppServerClient = btcPayAppServerClient;
        _logger = logger.CreateLogger<LDKPeerHandler>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _node.ConfigUpdated += ConfigUpdated;
        _descriptors.CollectionChanged += DescriptorsOnCollectionChanged;
        _ = ListenForInboundConnections(_cts.Token);
        _ = ContinuouslyAttemptToConnectToPersistentPeers(_cts.Token);
        _ = PeriodicTicker(_cts.Token, 1000, () => _peerManager.process_events());
        _btcPayAppServerClient.OnServerNodeInfo += BtcPayAppServerClientOnOnServerNodeInfo;
        if (!string.IsNullOrEmpty(_btcPayConnectionManager.ReportedNodeInfo))
        {
            _ = BtcPayAppServerClientOnOnServerNodeInfo(null, _btcPayConnectionManager.ReportedNodeInfo);
        }
    }

    private void DescriptorsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _node.PeersChanged();

    }

    private async Task BtcPayAppServerClientOnOnServerNodeInfo(object? sender, string e)
    {
        var nodeInfo = NodeInfo.Parse(e);
        var config = await _node.GetConfig();
        if (config.Peers.ContainsKey(nodeInfo.NodeId.ToString()))
            return;
        var endpoint = new IPEndPoint(IPAddress.Parse(nodeInfo.Host), nodeInfo.Port);
        await _node.Peer(nodeInfo.NodeId, new PeerInfo()
        {
            Label = "BTCPay Server Node",
            Endpoint = endpoint,
            Persistent = true,
            Trusted = true
        });
    }

    private TaskCompletionSource? _configTcs;

    private async Task ConfigUpdated(object? sender, LightningConfig e)
    {
        _configTcs?.TrySetResult();
    }

    private async Task ContinuouslyAttemptToConnectToPersistentPeers(CancellationToken ctsToken)
    {
        while (!ctsToken.IsCancellationRequested)
        {
            try
            {
            
            var connected = _peerManager.list_peers().Select(p => Convert.ToHexString(p.get_counterparty_node_id()).ToLower());
            var channels = (await _node.GetChannels(ctsToken)).Where(pair => pair.Value.channelDetails is not null)
                .Select(pair => pair.Value.channelDetails!).ToList();
            
            var channelPeers = channels
                .Select(details => Convert.ToHexString(details.get_counterparty().get_node_id()).ToLower()).Distinct();
            var config = await _node.GetConfig();
            var missingConnections = config.Peers
                .Where(pair => pair.Value.Persistent || channelPeers.Contains(pair.Key)).Select(pair => pair.Key)
                .Except(connected, StringComparer.InvariantCultureIgnoreCase).ToList();

            var tasks = new List<Task>();
            foreach (var persistentPeer in missingConnections)
            {
                var kv = config.Peers[persistentPeer];
                var nodeid = new PubKey(persistentPeer);
                if (kv.Endpoint is {} endpoint)
                {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(ctsToken);
                    cts.CancelAfter(10000);
                    tasks.Add(ConnectAsync(nodeid, endpoint, cts.Token));
                }
            }

            await Task.WhenAll(tasks);
            await Task.Delay(5000, ctsToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Error while attempting to connect to persistent peers");
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
            _peerManager.disconnect_all_peers();

        }, cancellationToken);
        _node.ConfigUpdated -= ConfigUpdated;

        _btcPayAppServerClient.OnServerNodeInfo -= BtcPayAppServerClientOnOnServerNodeInfo;

        _descriptors.CollectionChanged -= DescriptorsOnCollectionChanged;
    }

    private async Task ListenForInboundConnections(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            
            var config = await _node.GetConfig();
            if (!config.AcceptInboundConnection)
            {
                _configTcs ??= new();
                await _configTcs.Task.WaitAsync(cancellationToken);
                _configTcs = null;
               continue;
            }

            using var listener = new TcpListener(new IPEndPoint(IPAddress.Any, 0));
            listener.Start();
            var ip = listener.LocalEndpoint;
            Endpoint = new IPEndPoint(IPAddress.Loopback, (int) ip.Port());
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = LDKTcpDescriptor.Inbound(_peerManager,
                    await listener.AcceptTcpClientAsync(cancellationToken),
                    _logger, _descriptors);
                if (result is not null)
                {
                    _descriptors.TryAdd(result.Id, result);
                    _peerManager.process_events();
                }
            }
        }
    }

    public EndPoint? Endpoint { get; set; }

    public async Task<LDKTcpDescriptor?> ConnectAsync(NodeInfo nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var remote = IPEndPoint.Parse(nodeInfo.Host + ":" + nodeInfo.Port);
        return await ConnectAsync(nodeInfo.NodeId, remote, cancellationToken);
    }

    private readonly ConcurrentDictionary<string, Task<LDKTcpDescriptor?>> _connectionTasks = new();

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey peerNodeId,PeerInfo peerInfo, CancellationToken cancellationToken = default)
    {
        if (peerInfo.Endpoint is {} endpoint)
        {
            if(peerInfo.Label is not null)
                _logger.LogInformation($"Attempting to connect to {peerNodeId} at {endpoint} ({peerInfo.Label})");
            return await ConnectAsync(peerNodeId, endpoint, cancellationToken);
        }

        return null;
    }

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey theirNodeId, EndPoint remote,
        CancellationToken cancellationToken = default)
    {
        //cache this task so that we dont have multiple attempts to connect to the same place

        if (_connectionTasks.TryGetValue(theirNodeId.ToString(), out var task))
        {
            _logger.LogInformation($"Already attempting to connect to {theirNodeId}");
            return await task.WithCancellation(cancellationToken);
        }

        var tcs = new TaskCompletionSource<LDKTcpDescriptor?>();
        try
        {
            if (!_connectionTasks.TryAdd(theirNodeId.ToString(), tcs.Task))
            {
                return null;
            }

            if (_channelManager.get_our_node_id().SequenceEqual(theirNodeId.ToBytes()))
                return null;

            var client = new TcpClient();
            await client.ConnectAsync(remote.IPEndPoint(), cancellationToken);


            var result = LDKTcpDescriptor.Outbound(_peerManager, client, _logger, theirNodeId, _descriptors);
            if (result is not null)
            {
                _descriptors.TryAdd(result.Id, result);
                _peerManager.process_events();

                var config = await _node.GetConfig();
                if (!config.Peers.TryGetValue(theirNodeId.ToString(), out var peer))
                {
                    peer = new PeerInfo();
                }

                if (peer.Endpoint.ToEndpointString() != remote.ToString())
                {
                    peer.Endpoint = remote;
                    await _node.Peer(theirNodeId, peer);
                }
            }

            tcs.TrySetResult(result);
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
        finally
        {
            _connectionTasks.TryRemove(theirNodeId.ToString(), out _);
        }

        return await tcs.Task;
    }


    public async Task DisconnectAsync(PubKey id)
    {
       _logger.LogInformation($"Disconnecting from {id}");
       _peerManager.disconnect_by_node_id(id.ToBytes());
       _logger.LogInformation($"Disconnected from {id}");

    }
}
