using BTCPayApp.CommonServer;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Client.Models;
using Microsoft.Extensions.Logging;
using nldksample.LDK;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayAppServerClient : IBTCPayAppHubClient
{
    private readonly ILogger<BTCPayAppServerClient> _logger;
    private readonly PaymentsManager _paymentsManager;

    public BTCPayAppServerClient(ILogger<BTCPayAppServerClient> logger, PaymentsManager paymentsManager)
    {
        _logger = logger;
        _paymentsManager = paymentsManager;
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

    public async Task<LightningPayment> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest)
    {
       return await  _paymentsManager.RequestPayment(createLightningInvoiceRequest.Amount, createLightningInvoiceRequest.Expiry, createLightningInvoiceRequest.Description);
    }

    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<string>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    
    
}