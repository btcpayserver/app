using System.Text;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKAnnouncementBroadcaster: IScopedHostedService, ILDKEventHandler<Event.Event_ChannelReady>
{
    private readonly LDKPeerHandler _ldkPeerHandler;
    private readonly PeerManager _peerManager;
    private readonly ChannelManager _channelManager;
    private readonly LightningNodeService _lightningNodeService;
    private readonly CurrentWalletService _currentWalletService;
    private CancellationTokenSource? _cts;

    public LDKAnnouncementBroadcaster(LDKPeerHandler ldkPeerHandler, 
        PeerManager peerManager, ChannelManager channelManager, LightningNodeService lightningNodeService,
        CurrentWalletService currentWalletService)
    {
        _ldkPeerHandler = ldkPeerHandler;
        _peerManager = peerManager;
        _channelManager = channelManager;
        _lightningNodeService = lightningNodeService;
        _currentWalletService = currentWalletService;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RegularlyBroadcastAnnouncement(_cts.Token);
    }

    private TaskCompletionSource? _tcs;
    
    private async Task RegularlyBroadcastAnnouncement(CancellationToken cancellationToken)
    {
        while(cancellationToken.IsCancellationRequested == false)
        {
            if (_channelManager.list_channels().Any(details => details.get_is_public()))
            {
                
                var endpoint = _ldkPeerHandler.Endpoint?.Endpoint();
                var alias = _currentWalletService.Wallet.Alias;
                _peerManager.broadcast_node_announcement(_currentWalletService.Wallet.RGB, 
                    Encoding.UTF8.GetBytes(alias), endpoint is null? Array.Empty<SocketAddress>(): new []{endpoint});
            }
            _tcs = new TaskCompletionSource();
            await Task.WhenAny(_tcs.Task, Task.Delay(TimeSpan.FromMinutes(10), cancellationToken));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await (_cts?.CancelAsync() ?? Task.CompletedTask);
    }

    public async Task Handle(Event.Event_ChannelReady @event)
    {
        _tcs?.TrySetResult();
    }
}