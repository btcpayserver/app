using BTCPayApp.CommonServer;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayAppServerClient : IBTCPayAppHubClient
{
    private readonly ILogger<BTCPayAppServerClient> _logger;

    public BTCPayAppServerClient(ILogger<BTCPayAppServerClient> logger)
    {
        _logger = logger;
    }

    public async Task NotifyNetwork(string network)
    {
        _logger.LogInformation("NotifyNetwork: {network}", network);
        await OnNotifyNetwork?.Invoke(this, network);
        
    }

    public async Task TransactionDetected(string identifier, string txid)
    {
        _logger.LogInformation("OnTransactionDetected: {Txid}", txid);
        await OnTransactionDetected?.Invoke(this, txid);
    }

    public async Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {block}", block);
        await OnNewBlock?.Invoke(this, block);
    }

    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<string>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    
    
}