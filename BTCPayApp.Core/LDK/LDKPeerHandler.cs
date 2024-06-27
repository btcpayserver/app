using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using BTCPayApp.Core.Attempt2;
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
            Endpoint = endpoint.ToString(),
            Persistent = true,
            Trusted = true
        });
    }

    private CancellationTokenSource _configCts = new();

    private async Task ConfigUpdated(object? sender, LightningConfig e)
    {
        await _configCts.CancelAsync();
        _configCts = new();
    }

    private async Task ContinuouslyAttemptToConnectToPersistentPeers(CancellationToken ctsToken)
    {
        while (!ctsToken.IsCancellationRequested)
        {
            var connected = _peerManager.list_peers().Select(p => Convert.ToHexString(p.get_counterparty_node_id()).ToLower());
            var channels = await _node.GetChannels(ctsToken);
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
                if (EndPointParser.TryParse(kv.Endpoint, 9735, out var endpoint))
                {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(ctsToken);
                    cts.CancelAfter(10000);
                    tasks.Add(ConnectAsync(nodeid, endpoint, cts.Token));
                }
            }

            await Task.WhenAll(tasks);
            await Task.Delay(5000, ctsToken);
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
                await CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _configCts.Token).Token;
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

            _logger.LogInformation($"Connecting to {theirNodeId} at {remote}");
            var client = new TcpClient();
            await client.ConnectAsync(remote.IPEndPoint(), cancellationToken);


            _logger.LogInformation(
                $"{remote} {client.Connected} {client.Client.Connected} {client.Client.RemoteEndPoint} {client.Client.LocalEndPoint}");
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

                if (peer.Endpoint != remote.ToString())
                {
                    peer.Endpoint = remote.ToString()!;
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
