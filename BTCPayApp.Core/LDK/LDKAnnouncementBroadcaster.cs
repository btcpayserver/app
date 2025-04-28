using System.Text;
using BTCPayApp.Core.Helpers;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

// a background service that periodically checks if we have any public channels if so, publish a node announcement to the lightning network to be discoverable.
public class LDKAnnouncementBroadcaster(
    LDKPeerHandler ldkPeerHandler,
    PeerManager peerManager,
    LDKNode ldkNode)
    : IScopedHostedService, ILDKEventHandler<Event.Event_ChannelReady>
{
    private CancellationTokenSource? _cts;
    private TaskCompletionSource? _tcs;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RegularlyBroadcastAnnouncement(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task RegularlyBroadcastAnnouncement(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            var channels = (await ldkNode.GetChannels(cancellationToken) ?? [])
                .Where(pair => pair.channelDetails is not null)
                .Select(pair => pair.channelDetails!).ToList();

            if (channels.Any(details => details.get_is_announced()))
            {
                var endpoint = ldkPeerHandler.Endpoint?.Endpoint();
                var config = await ldkNode.GetConfig();
                var alias = config.Alias;
                peerManager.broadcast_node_announcement(config.RGB,
                    Encoding.UTF8.GetBytes(alias), endpoint is null ? [] : [endpoint]);
            }

            _tcs = new TaskCompletionSource();
            await Task.WhenAny(_tcs.Task, Task.Delay(TimeSpan.FromMinutes(10), cancellationToken));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await (_cts?.CancelAsync().WithCancellation(cancellationToken) ?? Task.CompletedTask);
    }

    public Task Handle(Event.Event_ChannelReady @event)
    {
        _tcs?.TrySetResult();
        return Task.CompletedTask;
    }
}
