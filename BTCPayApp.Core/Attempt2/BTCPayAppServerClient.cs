using System.Text;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using org.ldk.structs;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayAppServerClient(ILogger<BTCPayAppServerClient> _logger, IServiceProvider _serviceProvider)
    : IBTCPayAppHubClient
{
    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    public event AsyncEventHandler<string>? OnServerNodeInfo;
    public event AsyncEventHandler<long?>? OnMasterUpdated;
    public event AsyncEventHandler<ServerEvent>? OnNotifyServerEvent;

    public async Task NotifyServerEvent(ServerEvent ev)
    {
        _logger.LogInformation("NotifyServerEvent: {ev}", ev);
        await OnNotifyServerEvent?.Invoke(this, ev);
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

    public async Task<LightningInvoice> CreateInvoice(CreateLightningInvoiceRequest createLightningInvoiceRequest)
    {
        var descHash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(createLightningInvoiceRequest.Description)),
            false);
        return (await PaymentsManager.RequestPayment(createLightningInvoiceRequest.Amount,
            createLightningInvoiceRequest.Expiry, descHash)).ToInvoice();
    }

    public async Task<LightningInvoice?> GetLightningInvoice(uint256 paymentHash)
    {
        var invs = await PaymentsManager.List(payments =>
            payments.Where(payment => payment.Inbound && payment.PaymentHash == paymentHash));
        return invs.FirstOrDefault()?.ToInvoice();
    }

    public async Task<LightningPayment?> GetLightningPayment(uint256 paymentHash)
    {
        var invs = await PaymentsManager.List(payments =>
            payments.Where(payment => !payment.Inbound && payment.PaymentHash == paymentHash));
        return invs.FirstOrDefault()?.ToPayment();
    }

    public async Task CancelInvoice(uint256 paymentHash)
    {
        await PaymentsManager.CancelInbound(paymentHash);
    }

    public async Task<List<LightningPayment>> GetLightningPayments(ListPaymentsParams request)
    {
        return await PaymentsManager.List(payments => payments.Where(payment => !payment.Inbound), default)
            .ToPayments();
    }

    public async Task<List<LightningInvoice>> GetLightningInvoices(ListInvoicesParams request)
    {
        return await PaymentsManager.List(payments => payments.Where(payment => payment.Inbound), default).ToInvoices();
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

    public async Task MasterUpdated(long? deviceIdentifier)
    {
        _logger.LogInformation("MasterUpdated: {deviceIdentifier}", deviceIdentifier);
        OnMasterUpdated?.Invoke(this, deviceIdentifier);
    }

    public async Task<LightningNodeInformation> GetLightningNodeInfo()
    {
        var node = _serviceProvider.GetRequiredService<LightningNodeManager>().Node;
        var bb = await _serviceProvider.GetRequiredService<OnChainWalletManager>().GetBestBlock();
        var config = await node.GetConfig();
        var peers = await node.GetPeers();
        var channels = (await node.GetChannels()).Where(channel => channel.Value.channelDetails is not null)
            .Select(channel => channel.Value.channelDetails).ToArray();
        return new LightningNodeInformation()
        {
            Alias = config.Alias,
            Color = config.Color,
            Version = "preprepreprealpha",
            BlockHeight = bb.BlockHeight,
            PeersCount = peers.Length,
            ActiveChannelsCount = channels.Count(channel => channel.get_is_usable()),
            InactiveChannelsCount =
                channels.Count(channel => !channel.get_is_usable() && channel.get_is_channel_ready()),
            PendingChannelsCount =
                channels.Count(channel => !channel.get_is_usable() && !channel.get_is_channel_ready())
        };
    }

    public async Task<LightningNodeBalance> GetLightningBalance()
    {
        var channels = (await _serviceProvider.GetRequiredService<LightningNodeManager>().Node.GetChannels())
            .Where(channel => channel.Value.channelDetails is not null).Select(channel => channel.Value.channelDetails)
            .ToArray();

        return new LightningNodeBalance()
        {
            OffchainBalance = new OffchainBalance()
            {
                Local = LightMoney.MilliSatoshis(channels.Sum(channel => channel.get_balance_msat())),
                Remote = LightMoney.MilliSatoshis(channels.Sum(channel => channel.get_inbound_capacity_msat())),
            }
        };
    }
}