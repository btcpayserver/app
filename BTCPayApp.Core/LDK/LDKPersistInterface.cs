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
    
    public ChannelMonitorUpdateStatus persist_new_channel(OutPoint channel_funding_outpoint, ChannelMonitor data,
        MonitorUpdateId update_id)
    {
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
                var trackTask = _node.TrackScripts(outs).ContinueWith(task => _logger.LogDebug($"Tracking scripts finished for  updateid: {update_id.hash()}"));;
                var updateTask = _node.UpdateChannel(id, data.write()).ContinueWith(task => _logger.LogDebug($"Updating channel finished for  updateid: {update_id.hash()}"));;
                await  Task.WhenAll(trackTask, updateTask);
                
                await Task.Run(() =>
                {
                    _logger.LogDebug(
                        $"Calling channel_monitor_updated, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");

                    _serviceProvider.GetRequiredService<ChainMonitor>()
                        .channel_monitor_updated(channel_funding_outpoint, update_id);
                    _logger.LogDebug(
                        $"Persisted new channel, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");
                    updateTasks.TryRemove(updateId, out _);
                });

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

            await Task.Run(() =>
            {
                _logger.LogDebug(
                    $"Calling channel_monitor_updated, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");

                _serviceProvider.GetRequiredService<ChainMonitor>()
                    .channel_monitor_updated(channel_funding_outpoint, update_id);
                _logger.LogDebug(
                    $"Persisted update channel, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");
                updateTasks.TryRemove(updateId, out _);
            });

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