using BTCPayApp.Core.Helpers;
using org.ldk.structs;
using EventHandler = org.ldk.structs.EventHandler;

namespace BTCPayApp.Core.LDK;

public class LDKBackgroundProcessor : IScopedHostedService
{
    private readonly Persister _persister;
    private readonly EventHandler _eventHandler;
    private readonly ChainMonitor _chainMonitor;
    private readonly ChannelManager _channelManager;
    private readonly GossipSync _gossipSync;
    private readonly PeerManager _peerManager;
    private readonly Logger _logger;
    private readonly WriteableScore _scorer;
    private BackgroundProcessor? _processor;

    public LDKBackgroundProcessor(Persister persister,
        EventHandler eventHandler,
        ChainMonitor chainMonitor,
        ChannelManager channelManager,
        GossipSync gossipSync,
        PeerManager peerManager,
        Logger logger,
        WriteableScore scorer)
    {
        _persister = persister;
        _eventHandler = eventHandler;
        _chainMonitor = chainMonitor;
        _channelManager = channelManager;
        _gossipSync = gossipSync;
        _peerManager = peerManager;
        _logger = logger;
        _scorer = scorer;
    }
    
    
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.Register(o =>
        {
            _ = StopAsync(CancellationToken.None);
        }, null);
        _processor = BackgroundProcessor.start(_persister, _eventHandler, _chainMonitor, _channelManager, _gossipSync, _peerManager, _logger, Option_WriteableScoreZ.some(_scorer));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _processor?.stop();
    }
}