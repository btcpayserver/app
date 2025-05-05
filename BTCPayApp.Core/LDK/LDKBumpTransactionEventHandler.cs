using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKBumpTransactionEventHandler(BumpTransactionEventHandler bumpTransactionEventHandler) : ILDKEventHandler<Event.Event_BumpTransaction>
{
    public Task Handle(Event.Event_BumpTransaction @event)
    {
        bumpTransactionEventHandler.handle_event(@event.bump_transaction);
        return Task.CompletedTask;
    }
}
