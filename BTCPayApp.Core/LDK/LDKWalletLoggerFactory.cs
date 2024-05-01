using BTCPayApp.Core.Data;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.LDK;

public class LDKWalletLoggerFactory : ILoggerFactory
{
    private readonly LightningNodeService _lightningNodeService;
    private readonly ILoggerFactory _inner;

    public LDKWalletLoggerFactory(LightningNodeService lightningNodeService, ILoggerFactory loggerFactory)
    {
        _lightningNodeService = lightningNodeService;
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
        var categoryName = (string.IsNullOrWhiteSpace(category) ? "LDK" : $"LDK.{category}") +
                           $"[{_lightningNodeService.CurrentWallet}]";
        LoggerWrapper logger = new LoggerWrapper(_inner.CreateLogger(categoryName));

        logger.LogEvent += (sender, message) =>
            Logs.Add(DateTime.Now.ToShortTimeString() + " " + categoryName + message);

        return logger;
    }
}