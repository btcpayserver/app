using System.Collections.Concurrent;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using org.ldk.enums;
using org.ldk.structs;
using OutPoint = org.ldk.structs.OutPoint;
using Script = NBitcoin.Script;

namespace BTCPayApp.Core.LDK;

public class LDKPersistInterface : PersistInterface//, IScopedHostedService
{
    private readonly LDKNode _node;
    private readonly ILogger<LDKPersistInterface> _logger;

    private readonly IServiceProvider _serviceProvider;
    // private readonly ChainMonitor _chainMonitor;

    public LDKPersistInterface(LDKNode node, ILogger<LDKPersistInterface> logger, IServiceProvider serviceProvider  )
    {
        _node = node;
        _logger = logger;
        _serviceProvider = serviceProvider;
        // _chainMonitor = chainMonitor;
    }

    private ConcurrentDictionary<long, Task> updateTasks = new();
    
    // private async Task ProcessPendingUpdates()
    // {
    //
    //
    //     foreach (var updateTask in updateTasks)
    //     {
    //         if (updateTask.Value.IsCompleted)
    //         {
    //             _chainMonitor.channel_monitor_updated()
    //             updateTasks.TryRemove(updateTask.Key, out _);
    //         }
    //     }
    //     
    //     
    //     
    //     //TODO: carefully look at the upodate tasks and what chainmonitor tells us, and try to persist the monitor to resolve any jams
    //      var pending = _chainMonitor.list_pending_monitor_updates();
    //
    //     foreach (var monitorUpdateIdZze in pending)
    //     {
    //         var outpoint = monitorUpdateIdZze.get_a();
    //         var update = monitorUpdateIdZze.get_b();
    //         foreach (var updateId in update)
    //         {
    //             var data = _chainMonitor.get_monitor(outpoint);
    //             if (data is Result_LockedChannelMonitorNoneZ.Result_LockedChannelMonitorNoneZ_Err err)
    //             {
    //                 _logger.LogError($"Error getting monitor for outpoint {outpoint.Outpoint()}");
    //                 continue;
    //             }else if(data is Result_LockedChannelMonitorNoneZ.Result_LockedChannelMonitorNoneZ_OK ok)
    //             {
    //                 ok.
    //             }
    //             var status = update_persisted_channel(outpoint, data, monitorUpdateIdZze.get_c());
    //             if (status == ChannelMonitorUpdateStatus.LDKChannelMonitorUpdateStatus_Completed)
    //             {
    //                 _chainMonitor.channel_monitor_updated(outpoint, updateId);
    //             }
    //         }
    //         
    //     }
    // }
    
    
    public ChannelMonitorUpdateStatus persist_new_channel(OutPoint channel_funding_outpoint, ChannelMonitor data,
        MonitorUpdateId update_id)
    {
        //TODO: store update id  so that we can do this async
        
        try
        {
            _logger.LogDebug(
                $"Persisting new channel, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");
            
            var updateId = update_id.hash();

            var taskResult = updateTasks.GetOrAdd(updateId, async l =>
            {

                var outs = data.get_outputs_to_watch()
                    .SelectMany(zzzz => zzzz.get_b().Select(zz => Script.FromBytesUnsafe(zz.get_b()))).ToArray();

                var id = Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a());
                var trackTask = _node.TrackScripts(outs);
                var updateTask = _node.UpdateChannel(id, data.write());
                await  Task.WhenAll(trackTask, updateTask);
                _serviceProvider.GetRequiredService<ChainMonitor>().channel_monitor_updated(channel_funding_outpoint, update_id);
                // _chainMonitor.channel_monitor_updated(channel_funding_outpoint, update_id);
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

        var taskResult = updateTasks.GetOrAdd(updateId, async l =>
        {
            await _node.UpdateChannel(
                Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a()),
                data.write());
            _serviceProvider.GetRequiredService<ChainMonitor>().channel_monitor_updated(channel_funding_outpoint, update_id);
        });

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
    //
    // public async Task StartAsync(CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // public async Task StopAsync(CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
}