using BTCPayApp.Core.Attempt2;
using org.ldk.structs;
using UInt128 = org.ldk.util.UInt128;

namespace BTCPayApp.Core.LDK;

public class LDKChannelEventsHandler: 
    ILDKEventHandler<Event.Event_ChannelClosed>,
    ILDKEventHandler<Event.Event_ChannelPending>,
    ILDKEventHandler<Event.Event_ChannelReady>
{
    private readonly ChannelManager _channelManager;

    public LDKChannelEventsHandler(ChannelManager channelManager)
    {
        _channelManager = channelManager;
    }

    public async Task Handle(Event.Event_ChannelClosed @event)
    {
        throw new NotImplementedException();
    }

    public async Task Handle(Event.Event_ChannelPending @event)
    {
        throw new NotImplementedException();
    }

    public async Task Handle(Event.Event_ChannelReady @event)
    {
        throw new NotImplementedException();
    }
}


public class LDKOpenChannelRequestEventHandler: ILDKEventHandler<Event.Event_OpenChannelRequest>
{
    private readonly ChannelManager _channelManager;
    private readonly LDKNode _node;

    public LDKOpenChannelRequestEventHandler(ChannelManager channelManager, LDKNode node)
    {
        _channelManager = channelManager;
        _node = node;
    }
    public async Task Handle(Event.Event_OpenChannelRequest eventOpenChannelRequest)
    {
        var userChannelId = new UInt128(eventOpenChannelRequest.temporary_channel_id.Take(16).ToArray());
        
        if (eventOpenChannelRequest.channel_type.supports_zero_conf())
        {
           var nodeId =  Convert.ToHexString(eventOpenChannelRequest.counterparty_node_id);
            
            var config = await _node.GetConfig();
            if(config.Peers.TryGetValue(nodeId, out var peer) && peer.Trusted)
            {
                _channelManager.accept_inbound_channel_from_trusted_peer_0conf(
                    eventOpenChannelRequest.temporary_channel_id,
                    eventOpenChannelRequest.counterparty_node_id,
                    userChannelId
                );
                return;
            }
        }
        
        
        _channelManager.accept_inbound_channel(
            eventOpenChannelRequest.temporary_channel_id,
            eventOpenChannelRequest.counterparty_node_id,
            userChannelId);
        
        //TODO: if we want to reject the channel, we can call reject_channel
        //_channelManager.force_close_without_broadcasting_txn(eventOpenChannelRequest.temporary_channel_id, eventOpenChannelRequest.counterparty_node_id);
        
    }
}