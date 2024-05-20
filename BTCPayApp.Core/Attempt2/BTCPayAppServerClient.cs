using System.Text;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using LightningPayment = BTCPayApp.CommonServer.LightningPayment;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayAppServerClient : IBTCPayAppHubClient
{
    private readonly ILogger<BTCPayAppServerClient> _logger;
    private readonly IServiceProvider _serviceProvider;

    public BTCPayAppServerClient(ILogger<BTCPayAppServerClient> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task NotifyNetwork(string network)
    {
        _logger.LogInformation("NotifyNetwork: {network}", network);
        await OnNotifyNetwork?.Invoke(this, network);

    }

    public async Task TransactionDetected(TransactionDetectedRequest request)
    {
        _logger.LogInformation($"OnTransactionDetected: {request.TxId}" );
        await OnTransactionDetected?.Invoke(this, request);
    }


    public async Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {block}", block);
        await OnNewBlock?.Invoke(this, block);
    }
    
    private PaymentsManager PaymentsManager => _serviceProvider.GetRequiredService<LightningNodeManager>().Node.ServiceProvider.GetRequiredService<PaymentsManager>();

    public async Task<LightningPayment> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest)
    {
        var descHash =  new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(createLightningInvoiceRequest.Description)), false);
        return await PaymentsManager.RequestPayment(createLightningInvoiceRequest.Amount, createLightningInvoiceRequest.Expiry, descHash);
    }

    public async Task<LightningPayment?> GetLightningInvoice(string paymentHash)
    {
        var invs = await PaymentsManager.List(payments =>
            payments.Where(payment => payment.Inbound && payment.PaymentHash == paymentHash));
        return invs.FirstOrDefault();

    }

    public async Task<LightningPayment?> GetLightningPayment(string paymentHash)
    {
        var invs = await PaymentsManager.List(payments =>
            payments.Where(payment => !payment.Inbound && payment.PaymentHash == paymentHash));
        return invs.FirstOrDefault();
    }

    public async Task<List<LightningPayment>> GetLightningPayments(ListPaymentsParams request)
    {
        return await PaymentsManager.List(payments => payments.Where(payment => !payment.Inbound), default);
    }

    public async Task<List<LightningPayment>> GetLightningInvoices(ListInvoicesParams request)
    {
        return await PaymentsManager.List(payments => payments.Where(payment => payment.Inbound), default);
    }

    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;


}
