using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.LDK;

public class LDKWalletLoggerFactory(ILoggerFactory loggerFactory) : ILoggerFactory
{
    public void Dispose()
    {
        //ignore as this is scoped
    }

    public void AddProvider(ILoggerProvider provider)
    {
        loggerFactory.AddProvider(provider);
    }

    public List<string> Logs { get; } = new List<string>();

    public ILogger CreateLogger(string category)
    {
        var categoryName = string.IsNullOrWhiteSpace(category) ? "LDK" : $"LDK.{category}";
        LoggerWrapper logger = new LoggerWrapper(loggerFactory.CreateLogger(categoryName));

        logger.LogEvent += (sender, message) =>
            Logs.Add(DateTime.Now.ToShortTimeString() + " " + categoryName + message);

        return logger;
    }
}
