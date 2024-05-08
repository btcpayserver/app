using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NBitcoin;
using nldksample;
using nldksample.LDK;
using org.ldk.structs;
using WalletWasabi.Userfacing;
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

    public LDKTcpDescriptor[] ActiveDescriptors => _descriptors.Values.ToArray();

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
        _ = ListenForInboundConnections(_cts.Token);
        _ = ContinuouslyAttemptToConnectToPersistentPeers(_cts.Token);
        _ = PeriodicTicker(_cts.Token, 1000, () => _peerManager.process_events());
    }

    private async Task ContinuouslyAttemptToConnectToPersistentPeers(CancellationToken ctsToken)
    {
        while (!ctsToken.IsCancellationRequested)
        {
                var connected = _peerManager.get_peer_node_ids().Select(p => Convert.ToHexString(p.get_a()));
                var channelPeers =
                    _channelManager.list_channels()
                        .Select(details => Convert.ToHexString(details.get_counterparty().get_node_id())).Distinct();
                var missingConnections = _node.Config.Peers.Where(pair => pair.Value.Persistent || channelPeers.Contains(pair.Key) ).Select(pair => pair.Key).Except(connected, StringComparer.InvariantCultureIgnoreCase).ToList();
          
                var tasks = new List<Task>();
                foreach (var persistentPeer in missingConnections)
                {
              
                    var kv = _node.Config.Peers[persistentPeer];
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
    }

    private async Task ListenForInboundConnections(CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(new IPEndPoint(IPAddress.Any, 0));
        listener.Start();
        var ip = listener.LocalEndpoint;
        Endpoint = new IPEndPoint(IPAddress.Loopback, (int) ip.Port());
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = LDKTcpDescriptor.Inbound(_peerManager, await listener.AcceptTcpClientAsync(cancellationToken),
                _logger, _descriptors);
            if (result is not null)
            {
                _descriptors.TryAdd(result.Id, result);
                _peerManager.process_events();
            }
        }
    }

    public EndPoint Endpoint { get; set; }

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
        }

        return result;
    }
}