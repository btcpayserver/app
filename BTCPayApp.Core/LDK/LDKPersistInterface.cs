using System.Collections.Concurrent;
using BTCPayApp.Core.Attempt2;
using Microsoft.Extensions.Logging;
using org.ldk.enums;
using org.ldk.structs;
using OutPoint = org.ldk.structs.OutPoint;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKPersistInterface : PersistInterface
{
    private readonly LDKNode _node;
    private readonly ILogger<LDKPersistInterface> _logger;

    public LDKPersistInterface(LDKNode node, ILogger<LDKPersistInterface> logger)
    {
        _node = node;
        _logger = logger;
    }

    private ConcurrentDictionary<long, Task> updateTasks = new();
    
    public ChannelMonitorUpdateStatus persist_new_channel(OutPoint channel_funding_outpoint, ChannelMonitor data,
        MonitorUpdateId update_id)
    {
        //TODO: store update id  so that we can do this async
        
        try
        {
            _logger.LogDebug(
                $"Persisting new channel, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");
            
            var updateId = update_id.hash();

            var taskResult = updateTasks.GetOrAdd(updateId, l =>
            {

                var outs = data.get_outputs_to_watch()
                    .SelectMany(zzzz => zzzz.get_b().Select(zz => Script.FromBytesUnsafe(zz.get_b()))).ToArray();

                var id = Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a());
                var trackTask = _node.TrackScripts(outs);
                var updateTask = _node.UpdateChannel(id, data.write());
                return Task.WhenAll(trackTask, updateTask);
            });

            if (taskResult.IsFaulted)
            {
                _logger.LogError(taskResult.Exception, "Error persisting new channel");
                updateTasks.TryRemove(updateId, out _);
                return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_UnrecoverableError;
            }

            if (taskResult.IsCompleted)
            {
                
                updateTasks.TryRemove(updateId, out _);
                return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
                
            }
            
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_InProgress;
            
        }
        catch (Exception e)
        {
            
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_UnrecoverableError;
        }

    }


    public ChannelMonitorUpdateStatus update_persisted_channel(OutPoint channel_funding_outpoint, ChannelMonitorUpdate update,
        ChannelMonitor data, MonitorUpdateId update_id)
    {
        
        var updateId = update_id.hash();

        var taskResult = updateTasks.GetOrAdd(updateId, l => _node.UpdateChannel(
            Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a()),
            data.write()));

        if (taskResult.IsFaulted)
        {
            _logger.LogError(taskResult.Exception, "Error persisting channel update");
            updateTasks.TryRemove(updateId, out _);
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_UnrecoverableError;
        }

        if (taskResult.IsCompleted)
        {
                
            updateTasks.TryRemove(updateId, out _);
            return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed;
                
        }
        
        return ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_InProgress;
    }

    public void archive_persisted_channel(OutPoint channel_funding_outpoint)
    {
        //TODO: add archive column to channels table
    }
}