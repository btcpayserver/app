using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using org.ldk.structs;
using UInt128 = org.ldk.util.UInt128;

namespace BTCPayApp.Core.LDK;



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
        
        var userChannelId = new UInt128(eventOpenChannelRequest.temporary_channel_id.get_a().Take(16).ToArray());
        
        if (eventOpenChannelRequest.channel_type.supports_zero_conf())
        {
           var nodeId =  Convert.ToHexString(eventOpenChannelRequest.counterparty_node_id).ToLower();
            
            var config = await _node.GetConfig();
            if(config.Peers.TryGetValue(nodeId, out var peer) && peer.Trusted)
            {
               var result =  _channelManager.accept_inbound_channel_from_trusted_peer_0conf(
                    eventOpenChannelRequest.temporary_channel_id,
                    eventOpenChannelRequest.counterparty_node_id,
                    userChannelId
                );
               if(result is Result_NoneAPIErrorZ.Result_NoneAPIErrorZ_OK)
                    AcceptedChannel?.Invoke(this, eventOpenChannelRequest);
                return;
            }
        }
        
        
        _channelManager.accept_inbound_channel(
            eventOpenChannelRequest.temporary_channel_id,
            eventOpenChannelRequest.counterparty_node_id,
            userChannelId);
        
        AcceptedChannel?.Invoke(this, eventOpenChannelRequest);
        //TODO: if we want to reject the channel, we can call reject_channel
        //_channelManager.force_close_without_broadcasting_txn(eventOpenChannelRequest.temporary_channel_id, eventOpenChannelRequest.counterparty_node_id);
        
    }

    public AsyncEventHandler<Event.Event_OpenChannelRequest>? AcceptedChannel;
}