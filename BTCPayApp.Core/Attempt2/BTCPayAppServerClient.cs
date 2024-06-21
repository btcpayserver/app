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
using LightningPayment = BTCPayApp.CommonServer.Models.LightningPayment;

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

    public async Task NotifyServerEvent(string eventName)
    { 
        await OnNotifyServerEvent?.Invoke(this, eventName);
    }

    public async Task NotifyNetwork(string network)
    {
        _logger.LogInformation("NotifyNetwork: {network}", network);
        await OnNotifyNetwork?.Invoke(this, network);
    }

    public async Task NotifyServerNode(string nodeInfo)
    {
        _logger.LogInformation("NotifyServerNode: {nodeInfo}", nodeInfo);
        await OnServerNodeInfo?.Invoke(this, nodeInfo);
    }

    public async Task TransactionDetected(TransactionDetectedRequest request)
    {
        _logger.LogInformation($"OnTransactionDetected: {request.TxId}");
        await OnTransactionDetected?.Invoke(this, request);
    }


    public async Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {block}", block);
        await OnNewBlock?.Invoke(this, block);
    }

    private PaymentsManager PaymentsManager =>
        _serviceProvider.GetRequiredService<LightningNodeManager>().Node.PaymentsManager;

    public async Task<LightningPayment> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest)
    {
        var descHash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(createLightningInvoiceRequest.Description)),
            false);
        return await PaymentsManager.RequestPayment(createLightningInvoiceRequest.Amount,
            createLightningInvoiceRequest.Expiry, descHash);
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

    public async Task<PayResponse> PayInvoice(string bolt11, long? amountMilliSatoshi)
    {
        var network = _serviceProvider.GetRequiredService<OnChainWalletManager>().Network;
        var bolt = BOLT11PaymentRequest.Parse(bolt11, network);
        try
        {
            var result = await PaymentsManager.PayInvoice(bolt,
                amountMilliSatoshi is null ? null : LightMoney.MilliSatoshis(amountMilliSatoshi.Value));
            return new PayResponse()
                {
                    Result = result.Status switch
                    {
                        LightningPaymentStatus.Unknown => PayResult.Unknown,
                        LightningPaymentStatus.Pending => PayResult.Unknown,
                        LightningPaymentStatus.Complete => PayResult.Ok,
                        LightningPaymentStatus.Failed => PayResult.Error,
                        _ => throw new ArgumentOutOfRangeException()
                    },
                    Details = new PayDetails()
                    {
                        Preimage = result.Preimage is not null ? new uint256(result.Preimage) : null,
                        Status = result.Status
                    }
                };
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error paying invoice");
            return new PayResponse(PayResult.Error, e.Message);
        }
    }

    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    public event AsyncEventHandler<string>? OnServerNodeInfo;
    public event AsyncEventHandler<string>? OnNotifyServerEvent;
}