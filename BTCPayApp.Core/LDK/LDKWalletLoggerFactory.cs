using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.LDK;

public class LDKWalletLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;

    public LDKWalletLoggerFactory(ILoggerFactory loggerFactory)
    {
        _inner = loggerFactory;
    }

    public void Dispose()
    {
        //ignore as this is scoped
    }

    public void AddProvider(ILoggerProvider provider)
    {
        _inner.AddProvider(provider);
    }

    public List<string> Logs { get; } = new List<string>();

    public ILogger CreateLogger(string category)
    {
        var categoryName = string.IsNullOrWhiteSpace(category) ? "LDK" : $"LDK.{category}";
        LoggerWrapper logger = new LoggerWrapper(_inner.CreateLogger(categoryName));

        logger.LogEvent += (sender, message) =>
            Logs.Add(DateTime.Now.ToShortTimeString() + " " + categoryName + message);

        return logger;
    }
}