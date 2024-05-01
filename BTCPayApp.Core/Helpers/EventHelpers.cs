namespace BTCPayApp.Core.Helpers;

public delegate Task AsyncEventHandler<TEventArgs>(object? sender, TEventArgs e);

public static class EventHandlers
{
    public static EventHandler<TArgs> TryAsync<TArgs>(
        this Func<object, TArgs, Task> callback)
        where TArgs : EventArgs
        => TryAsync<TArgs>(
            callback,
            ex => Task.CompletedTask);

    public static EventHandler<TArgs> TryAsync<TArgs>(
        this Func<object, TArgs, Task> callback,
        Action<Exception> errorHandler)
        where TArgs : EventArgs
        => TryAsync<TArgs>(
            callback,
            ex =>
            {
                errorHandler.Invoke(ex);
                return Task.CompletedTask;
            });

    public static EventHandler<TArgs> TryAsync<TArgs>(
        this Func<object, TArgs, Task> callback,
        Func<Exception, Task> errorHandler)
        where TArgs : EventArgs
    {
        return new EventHandler<TArgs>(async (object s, TArgs e) =>
        {
            try
            {
                await callback.Invoke(s, e);
            }
            catch (Exception ex)
            {
                await errorHandler.Invoke(ex);
            }
        });
    }
}