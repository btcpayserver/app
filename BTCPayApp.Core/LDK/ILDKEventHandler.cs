using org.ldk.structs;

namespace nldksample.LDK;

public interface ILDKEventHandler<in TEvent>: ILDKEventHandler where TEvent : Event
{
    Task Handle(TEvent @event);
}

public interface ILDKEventHandler
{
    Task Handle(Event @event)
    {
        var eventType = @event.GetType();
        
        var result = this.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ILDKEventHandler<>) && i.GetGenericArguments()[0] == eventType)
            ?.GetMethod(nameof(ILDKEventHandler<Event>.Handle))
            ?.Invoke(this, new object[] {@event});

        if (result is Task task)
            return task;
        return Task.CompletedTask;
    }
}
