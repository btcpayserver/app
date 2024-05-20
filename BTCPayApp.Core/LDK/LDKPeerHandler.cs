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
//TODO: If we have channels open, we should be storing the last endpoint that we connected to the counterpoarty uso that we attempt to keep a connection established. 
public class LDKPeerHandler : IScopedHostedService
{
    private readonly ILogger<LDKPeerHandler> _logger;
    private readonly PeerManager _peerManager;
    private readonly ChannelManager _channelManager;
    private readonly LDKNode _node;
    private CancellationTokenSource? _cts;


    readonly ObservableConcurrentDictionary<string, LDKTcpDescriptor> _descriptors = new();

    public LDKPeerHandler(PeerManager peerManager, LDKWalletLoggerFactory logger, ChannelManager channelManager, LDKNode node)
    {
        _peerManager = peerManager;
        _channelManager = channelManager;
        _node = node;
        _logger = logger.CreateLogger<LDKPeerHandler>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _node.ConfigUpdated += ConfigUpdated;
        _ = ListenForInboundConnections(_cts.Token);
        _ = ContinuouslyAttemptToConnectToPersistentPeers(_cts.Token);
        _ = PeriodicTicker(_cts.Token, 1000, () => _peerManager.process_events());
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
                var connected = _peerManager.list_peers().Select(p => Convert.ToHexString(p.get_counterparty_node_id()));
                var channelPeers =
                    _channelManager.list_channels()
                        .Select(details => Convert.ToHexString(details.get_counterparty().get_node_id())).Distinct();
                var config = await _node.GetConfig();
                var missingConnections = config.Peers.Where(pair => pair.Value.Persistent || channelPeers.Contains(pair.Key) ).Select(pair => pair.Key).Except(connected, StringComparer.InvariantCultureIgnoreCase).ToList();
          
                var tasks = new List<Task>();
                foreach (var persistentPeer in missingConnections)
                {
              
                    var kv = config.Peers[persistentPeer];
                    var nodeid = new PubKey(persistentPeer);
                    if(EndPointParser.TryParse(kv.Endpoint,9735, out var endpoint))
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
            await _cts.CancelAsync();

        _logger.LogInformation("Stopping, disconnecting all peers");
        _peerManager.disconnect_all_peers();
        _node.ConfigUpdated -= ConfigUpdated;
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

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey theirNodeId, EndPoint remote,
        CancellationToken cancellationToken = default)
    {
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
            if (peer.Endpoint != remote.ToString())
            {
                peer.Endpoint = remote.ToString()!;
                await _node.Peer(theirNodeId.ToString(), peer);
            }
        }

        return result;
    }
}