using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.LDK;

public class LoggerWrapper(ILogger inner) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return inner.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return inner.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        inner.Log(logLevel, eventId, state, exception, formatter);
        LogEvent?.Invoke(this, formatter(state, exception));
    }

    public event EventHandler<string>? LogEvent;
}
