using Microsoft.Extensions.Logging;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKEventHandler(IEnumerable<ILDKEventHandler> eventHandlers, LDKWalletLogger ldkWalletLogger) : EventHandlerInterface
{
    public Result_NoneReplayEventZ handle_event(Event @event)
    {
        ldkWalletLogger.LogInformation("Received event {Type}", @event.GetType());
        eventHandlers.AsParallel().ForAll(async void (handler) =>
        {
            try
            {
                await handler.Handle(@event);
            }
            catch (Exception ex)
            {
                ldkWalletLogger.LogError(ex, "Error handling event {EventType} with handler {HandlerType}", @event.GetType(), handler.GetType());
            }
        });
        return Result_NoneReplayEventZ.ok();
    }
}
