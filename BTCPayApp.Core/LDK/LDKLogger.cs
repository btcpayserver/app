using Microsoft.Extensions.Logging;
using org.ldk.enums;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKLogger(ILoggerFactory loggerFactory) : LoggerInterface, ILogger
{
    private readonly ILogger _baseLogger = loggerFactory.CreateLogger("");

    public virtual void log(Record record)
    {
        var level = record.get_level() switch
        {
            Level.LDKLevel_Trace => LogLevel.Trace,
            Level.LDKLevel_Debug => LogLevel.Debug,
            Level.LDKLevel_Info => LogLevel.Information,
            Level.LDKLevel_Warn => LogLevel.Warning,
            Level.LDKLevel_Error => LogLevel.Error,
            Level.LDKLevel_Gossip => LogLevel.Trace,
        };
         loggerFactory.CreateLogger(record.get_module_path()).Log(level, "{Args}", record.get_args());
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _baseLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _baseLogger.IsEnabled(logLevel);
    }

    public virtual void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _baseLogger.Log(logLevel, eventId, state, exception, formatter);
    }
}
