using BTCPayApp.Core.Helpers;
using org.ldk.structs;
using EventHandler = org.ldk.structs.EventHandler;

namespace BTCPayApp.Core.LDK;
/// <summary>
/// Runs the LDK background processor which handles the main event loop for the LDK library.
/// </summary>
public class LDKBackgroundProcessor(
    Persister persister,
    EventHandler eventHandler,
    ChainMonitor chainMonitor,
    ChannelManager channelManager,
    OnionMessenger onionMessenger,
    GossipSync gossipSync,
    PeerManager peerManager,
    Logger logger,
    WriteableScore scorer)
    : IScopedHostedService
{
    private BackgroundProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(CancellationToken.None);
        _processor = BackgroundProcessor.start(persister, eventHandler, chainMonitor, channelManager, onionMessenger, gossipSync, peerManager, logger, Option_WriteableScoreZ.some(scorer));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _processor?.stop();
        return Task.CompletedTask;
    }
}
