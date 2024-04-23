using System.Collections.Concurrent;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKPendingHTLCsForwardableEventHandler : ILDKEventHandler<Event.Event_PendingHTLCsForwardable>
{
    private readonly ChannelManager _channelManager;

    public ConcurrentBag<(DateTimeOffset, Func<Task>)> ScheduledTasks { get; } = new();

    public LDKPendingHTLCsForwardableEventHandler(ChannelManager channelManager)
    {
        _channelManager = channelManager;
    }

    public async Task Handle(Event.Event_PendingHTLCsForwardable eventPendingHtlCsForwardable)
    {
        var time = Random.Shared.NextInt64(eventPendingHtlCsForwardable.time_forwardable,
            5 * eventPendingHtlCsForwardable.time_forwardable);
        ScheduledTasks.Add((DateTimeOffset.UtcNow.AddSeconds(time), () =>
        {
            _channelManager.process_pending_htlc_forwards();
            return Task.CompletedTask;
        }));
    }
}