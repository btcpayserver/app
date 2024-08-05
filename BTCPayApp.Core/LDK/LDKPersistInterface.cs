﻿using System.Collections.Concurrent;
using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.enums;
using org.ldk.structs;
using VSSProto;
using OutPoint = org.ldk.structs.OutPoint;

namespace BTCPayApp.Core.LDK;

public class LDKPersistInterface : PersistInterface, IScopedHostedService
{
    private readonly LDKNode _node;
    private readonly ILogger<LDKPersistInterface> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly SyncService _syncService;
    private readonly ConcurrentDictionary<long, Task> updateTasks = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource> _updateTaskCompletionSources = new();
    // private readonly ChainMonitor _chainMonitor;

    public LDKPersistInterface(LDKNode node, ILogger<LDKPersistInterface> logger, IServiceProvider serviceProvider, SyncService syncService  )
    {
        _node = node;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _syncService = syncService;
        _syncService.RemoteObjectUpdated += SyncServiceOnRemoteObjectUpdated;
    }

    private Task SyncServiceOnRemoteObjectUpdated(object? sender, PutObjectRequest e)
    {
        var channelUpdates = e.TransactionItems.Where(x => x.Key.StartsWith("Channel_")).Select(value => JsonSerializer.Deserialize<Channel>(value.Value.ToStringUtf8())!).ToArray();
        foreach (var channelUpdate in channelUpdates)
        {
           var tcs =  _updateTaskCompletionSources.GetOrAdd(channelUpdate.Checkpoint, new TaskCompletionSource());
           tcs.SetResult();
        }
        return Task.CompletedTask;
    }

    
    
    
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
                //
                // var outs = data.get_outputs_to_watch()
                //     .SelectMany(zzzz => zzzz.get_b().Select(zz => Script.FromBytesUnsafe(zz.get_b()))).ToArray();

                _updateTaskCompletionSources.TryAdd(updateId, new TaskCompletionSource());
                var fundingId = Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a()).ToLower();
                
                var identifiers = new  List<ChannelAlias>();
                identifiers.Add(new ChannelAlias()
                {
                    Id = fundingId,
                    Type = "funding_outpoint"
                });
                var otherId = data.channel_id().is_zero()? null: Convert.ToHexString(data.channel_id().get_a()).ToLower();
                if(otherId == fundingId)
                {
                    otherId = null;
                    
                }
                if(otherId != null)
                {
                    identifiers.Add(new ChannelAlias()
                    {
                        Id = otherId,
                        Type = "arbitrary_id"
                    });
                }
                
                // var trackTask = _node.TrackScripts(outs).ContinueWith(task => _logger.LogDebug($"Tracking scripts finished for  updateid: {update_id.hash()}"));;
                var updateTask = _node.UpdateChannel(identifiers, data.write(), updateId).ContinueWith(task => _logger.LogDebug($"Updating channel finished for  updateid: {update_id.hash()}"));;
                await updateTask;
                _logger.LogDebug("channel updated to local, waiting for remote sync to finish");
                await _updateTaskCompletionSources[updateId].Task;
                _updateTaskCompletionSources.TryRemove(updateId, out _);
                await Task.Run(() =>
                {
                    try
                    {
                         
                    _logger.LogDebug(
                        $"Calling channel_monitor_updated, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");

                    _serviceProvider.GetRequiredService<ChainMonitor>()
                        .channel_monitor_updated(channel_funding_outpoint, update_id);
                    _logger.LogDebug(
                        $"Persisted new channel, outpoint: {channel_funding_outpoint.Outpoint()}, updateid: {update_id.hash()}");
                    updateTasks.TryRemove(updateId, out _);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error calling channel_monitor_updated for new channel ");
                        updateTasks.TryRemove(updateId, out _);
                    }
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

        _updateTaskCompletionSources.TryAdd(updateId, new TaskCompletionSource());
        var taskResult = updateTasks.GetOrAdd(updateId, async l =>
        {
            
            var fundingId = Convert.ToHexString(ChannelId.v1_from_funding_outpoint(channel_funding_outpoint).get_a()).ToLower();
                
            var identifiers = new  List<ChannelAlias>();
            identifiers.Add(new ChannelAlias()
            {
                Id = fundingId,
                Type = "funding_outpoint"
            });
            var otherId = data.channel_id().is_zero()? null: Convert.ToHexString(data.channel_id().get_a()).ToLower();
            if(otherId == fundingId)
            {
                otherId = null;
                    
            }
            if(otherId != null)
            {
                identifiers.Add(new ChannelAlias()
                {
                    Id = otherId,
                    Type = "arbitrary_id"
                });
            }
            
            await _node.UpdateChannel(identifiers, data.write(), updateId);
            _logger.LogDebug("channel updated to local, waiting for remote sync to finish");
            await _updateTaskCompletionSources[updateId].Task;
            _updateTaskCompletionSources.TryRemove(updateId, out _);
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
    public async Task StartAsync(CancellationToken cancellationToken)
    {
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _syncService.RemoteObjectUpdated -= SyncServiceOnRemoteObjectUpdated;
    }
}