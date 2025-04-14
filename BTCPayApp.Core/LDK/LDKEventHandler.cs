using Microsoft.Extensions.Logging;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKEventHandler : EventHandlerInterface
{
    private readonly IEnumerable<ILDKEventHandler> _eventHandlers;
    private readonly LDKWalletLogger _ldkWalletLogger;

    public LDKEventHandler(IEnumerable<ILDKEventHandler> eventHandlers, LDKWalletLogger ldkWalletLogger)
    {
        _eventHandlers = eventHandlers;
        _ldkWalletLogger = ldkWalletLogger;
    }

    public Result_NoneReplayEventZ handle_event(Event @event)
    {
        _ldkWalletLogger.LogInformation("Received event {Type}", @event.GetType());
        _eventHandlers.AsParallel().ForAll(async void (handler) =>
        {
            try
            {
                await handler.Handle(@event);
            }
            catch (Exception ex)
            {
                _ldkWalletLogger.LogError(ex, "Error handling event {EventType} with handler {HandlerType}", @event.GetType(), handler.GetType());
            }
        });
        return Result_NoneReplayEventZ.ok();
    }
}
