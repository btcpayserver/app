using BTCPayApp.Core.Helpers;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class LDKPendingHTLCsForwardableEventHandler : IScopedHostedService, ILDKEventHandler<Event.Event_PendingHTLCsForwardable>
{
    private readonly ChannelManager _channelManager;
    private readonly ConcurrentQueue<DateTimeOffset> _scheduledTimes;
    private readonly ILogger<LDKPendingHTLCsForwardableEventHandler> _logger;

    public LDKPendingHTLCsForwardableEventHandler(ChannelManager channelManager, ILogger<LDKPendingHTLCsForwardableEventHandler> logger)
    {
        _channelManager = channelManager;
        _scheduledTimes = new ConcurrentQueue<DateTimeOffset>();
        _logger = logger;
    }

    public Task Handle(Event.Event_PendingHTLCsForwardable eventPendingHtlCsForwardable)
    {
        var time = Random.Shared.NextInt64(eventPendingHtlCsForwardable.time_forwardable,
            5 * eventPendingHtlCsForwardable.time_forwardable);
        _logger.LogDebug($"Scheduling processing of pending HTLC forwards in {time} seconds");
        _scheduledTimes.Enqueue(DateTimeOffset.UtcNow.AddSeconds(time));
        return Task.CompletedTask;
    }

    protected async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (_scheduledTimes.TryDequeue(out var scheduledTime))
            {
                var delay = scheduledTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, stoppingToken);
                }

                _logger.LogDebug("Processing pending HTLC forwards");
               
                Task.Run(() => _channelManager.process_pending_htlc_forwards());
            }

            await Task.Delay(1000, stoppingToken); // Polling delay
        }
    }

    private CancellationTokenSource _cancellationTokenSource = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource= new CancellationTokenSource();
        _ = ExecuteAsync(_cancellationTokenSource.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync();
    }
}
