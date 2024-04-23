using nldksample.LSP.Flow;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKBumpTransactionEventHandler: ILDKEventHandler<Event.Event_BumpTransaction>
{
    private readonly BumpTransactionEventHandler _bumpTransactionEventHandler;

    public LDKBumpTransactionEventHandler(BumpTransactionEventHandler bumpTransactionEventHandler)
    {
        _bumpTransactionEventHandler = bumpTransactionEventHandler;
    }
    public Task Handle(Event.Event_BumpTransaction @event)
    {
        _bumpTransactionEventHandler.handle_event(@event.bump_transaction);
        return Task.CompletedTask;
    }
}