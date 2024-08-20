using System.Text;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKAnnouncementBroadcaster : IScopedHostedService, ILDKEventHandler<Event.Event_ChannelReady>
{
    private readonly LDKPeerHandler _ldkPeerHandler;
    private readonly PeerManager _peerManager;
    private readonly LDKNode _ldkNode;
    private CancellationTokenSource? _cts;

    public LDKAnnouncementBroadcaster(LDKPeerHandler ldkPeerHandler,
        PeerManager peerManager,  LDKNode ldkNode)
    {
        _ldkPeerHandler = ldkPeerHandler;
        _peerManager = peerManager;
        _ldkNode = ldkNode;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RegularlyBroadcastAnnouncement(_cts.Token);
    }

    private TaskCompletionSource? _tcs;

    private async Task RegularlyBroadcastAnnouncement(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            var channels = (await _ldkNode.GetChannels(cancellationToken)).Where(pair => pair.Value.channelDetails is not null)
                .Select(pair => pair.Value.channelDetails!).ToList();

            if (channels.Any(details => details.get_is_public()))
            {
                var endpoint = _ldkPeerHandler.Endpoint?.Endpoint();
                var config = await _ldkNode.GetConfig();
                var alias = config.Alias;
                _peerManager.broadcast_node_announcement(config.RGB,
                    Encoding.UTF8.GetBytes(alias), endpoint is null ? Array.Empty<SocketAddress>() : new[] {endpoint});
            }

            _tcs = new TaskCompletionSource();
            await Task.WhenAny(_tcs.Task, Task.Delay(TimeSpan.FromMinutes(10), cancellationToken));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await (_cts?.CancelAsync().WithCancellation(cancellationToken) ?? Task.CompletedTask);
    }

    public async Task Handle(Event.Event_ChannelReady @event)
    {
        _tcs?.TrySetResult();
    }
}