using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

/// <summary>
/// A typed variant of <see cref="ILDKEventHandler"/> that handles a specific type of event
/// </summary>
/// <typeparam name="TEvent"></typeparam>
public interface ILDKEventHandler<in TEvent>: ILDKEventHandler where TEvent : Event
{
    Task Handle(TEvent @event);
}
/// <summary>
/// Handles events published by LDK
/// </summary>
public interface ILDKEventHandler
{
    Task Handle(Event @event)
    {
        var eventType = @event.GetType();

        var result = GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ILDKEventHandler<>) && i.GetGenericArguments()[0] == eventType)
            ?.GetMethod(nameof(ILDKEventHandler<Event>.Handle))
            ?.Invoke(this, [@event]);

        if (result is Task task)
            return task;
        return Task.CompletedTask;
    }
}
