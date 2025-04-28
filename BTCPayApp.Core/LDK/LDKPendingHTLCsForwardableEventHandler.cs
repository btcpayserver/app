using Microsoft.Extensions.Logging;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using org.ldk.structs;
using System.Collections.Concurrent;

namespace BTCPayApp.Core.LDK;

public class LDKPendingHTLCsForwardableEventHandler(
    IServiceProvider serviceProvider,
    ILogger<LDKPendingHTLCsForwardableEventHandler> logger)
    : IScopedHostedService, ILDKEventHandler<Event.Event_PendingHTLCsForwardable>
{
    private readonly ConcurrentQueue<DateTimeOffset> _scheduledTimes = new();

    private CancellationTokenSource _cancellationTokenSource = new();

    public Task Handle(Event.Event_PendingHTLCsForwardable eventPendingHtlCsForwardable)
    {
        var time = Random.Shared.NextInt64(eventPendingHtlCsForwardable.time_forwardable,
            5 * eventPendingHtlCsForwardable.time_forwardable);
        logger.LogDebug("Scheduling processing of pending HTLC forwards in {Time} seconds", time);
        _scheduledTimes.Enqueue(DateTimeOffset.UtcNow.AddSeconds(time));
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource= new CancellationTokenSource();
        _ = ExecuteAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
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

                logger.LogDebug("Processing pending HTLC forwards");

                _ = Task.Run(() => serviceProvider.GetRequiredService<ChannelManager>().process_pending_htlc_forwards(), stoppingToken);
            }

            await Task.Delay(1000, stoppingToken); // Polling delay
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cancellationTokenSource.CancelAsync().WithCancellation(cancellationToken);
    }
}
