using System.Text;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayApp.Core.Wallet;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using org.ldk.structs;

namespace BTCPayApp.Core.BTCPayServer;


public class BTCPayAppServerClient(ILogger<BTCPayAppServerClient> _logger, IServiceProvider _serviceProvider)
    : IBTCPayAppHubClient
{
    public event AsyncEventHandler<string>? OnNewBlock;
    public event AsyncEventHandler<TransactionDetectedRequest>? OnTransactionDetected;
    public event AsyncEventHandler<string>? OnNotifyNetwork;
    public event AsyncEventHandler<string>? OnServerNodeInfo;
    public event AsyncEventHandler<long?>? OnMasterUpdated;
    public event AsyncEventHandler<ServerEvent>? OnNotifyServerEvent;

    private LDKNode? Node => _serviceProvider.GetRequiredService<LightningNodeManager>().Node;
    private PaymentsManager? PaymentsManager => Node?.PaymentsManager;
    private LightningAPIKeyManager? ApiKeyManager => Node?.ApiKeyManager;

    public async Task NotifyServerEvent(ServerEvent ev)
    {
        _logger.LogInformation("NotifyServerEvent: {Event}", ev.ToString());
        if (OnNotifyServerEvent is null) return;
        await OnNotifyServerEvent.Invoke(this, ev);
    }

    public async Task NotifyNetwork(string network)
    {
        _logger.LogInformation("NotifyNetwork: {Network}", network);
        if (OnNotifyNetwork is null) return;
        await OnNotifyNetwork.Invoke(this, network);
    }

    public async Task NotifyServerNode(string nodeInfo)
    {
        _logger.LogInformation("NotifyServerNode: {NodeInfo}", nodeInfo);
        if (OnServerNodeInfo is null) return;
        await OnServerNodeInfo.Invoke(this, nodeInfo);
    }

    public async Task TransactionDetected(TransactionDetectedRequest request)
    {
        _logger.LogInformation("OnTransactionDetected: {TxId}", request.TxId);
        if (OnTransactionDetected is null) return;
        await OnTransactionDetected.Invoke(this, request);
    }

    public async Task NewBlock(string block)
    {
        _logger.LogInformation("NewBlock: {Block}", block);
        if (OnNewBlock is null) return;
        await OnNewBlock.Invoke(this, block);
    }

    public async Task StartListen(string key)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        _serviceProvider
            .GetRequiredService<LightningNodeManager>().Node?
            .GetServiceProvider()
            .GetRequiredService<BTCPayPaymentsNotifier>()
            .StartListen();
    }

    private async Task AssertPermission(string key, APIKeyPermission permission)
    {
        if (ApiKeyManager is null)
            throw new HubException("Api Key Manager not available");
        if (!await ApiKeyManager.CheckPermission(key, permission))
            throw new HubException("Permission denied");
    }

    public async Task<LightningInvoice> CreateInvoice(string key, CreateLightningInvoiceRequest createLightningInvoiceRequest)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        var descHash = new uint256(Hashes.SHA256(Encoding.UTF8.GetBytes(createLightningInvoiceRequest.Description)),
            false);
        return (await PaymentsManager.RequestPayment(createLightningInvoiceRequest.Amount,
            createLightningInvoiceRequest.Expiry, descHash)).ToInvoice();
    }

    public async Task<LightningInvoice?> GetLightningInvoice(string key, uint256 paymentHash)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        var invoices = await PaymentsManager.List(payments =>
            payments.Where(payment => payment.Inbound && payment.PaymentHash == paymentHash));
        return invoices.FirstOrDefault()?.ToInvoice();
    }

    public async Task<LightningPayment?> GetLightningPayment(string key, uint256 paymentHash)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        var invoices = await PaymentsManager.List(payments =>
            payments.Where(payment => !payment.Inbound && payment.PaymentHash == paymentHash));
        return invoices.FirstOrDefault()?.ToPayment();
    }

    public async Task CancelInvoice(string key, uint256 paymentHash)
    {
        await AssertPermission(key, APIKeyPermission.Write);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        await PaymentsManager.CancelInbound(paymentHash);
    }

    public async Task<List<LightningPayment>> GetLightningPayments(string key, ListPaymentsParams request)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        return await PaymentsManager.List(payments => payments.Where(payment => !payment.Inbound))
            .ToPayments();
    }

    public async Task<List<LightningInvoice>> GetLightningInvoices(string key, ListInvoicesParams request)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        return await PaymentsManager.List(payments => payments.Where(payment => payment.Inbound)).ToInvoices();
    }

    public async Task<PayResponse> PayInvoice(string key, string bolt11, long? amountMilliSatoshi)
    {
        await AssertPermission(key, APIKeyPermission.Write);
        if (PaymentsManager is null) throw new HubException("Payments Manager not available");

        var config = await _serviceProvider.GetRequiredService<OnChainWalletManager>().GetConfig();
        var network = config?.NBitcoinNetwork;
        if (network is null) throw new HubException("Network info not available");

        var bolt = BOLT11PaymentRequest.Parse(bolt11, network);
        try
        {
            var result = await PaymentsManager.PayInvoice(bolt,
                amountMilliSatoshi is null ? null : LightMoney.MilliSatoshis(amountMilliSatoshi.Value));
            return new PayResponse
            {
                Result = result.Status switch
                {
                    LightningPaymentStatus.Unknown => PayResult.Unknown,
                    LightningPaymentStatus.Pending => PayResult.Unknown,
                    LightningPaymentStatus.Complete => PayResult.Ok,
                    LightningPaymentStatus.Failed => PayResult.Error,
                    _ => throw new ArgumentOutOfRangeException()
                },
                Details = new PayDetails
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

    public Task MasterUpdated(long? deviceIdentifier)
    {
        _logger.LogInformation("MasterUpdated: {DeviceIdentifier}", deviceIdentifier);
        OnMasterUpdated?.Invoke(this, deviceIdentifier);
        return Task.CompletedTask;
    }

    public async Task<LightningNodeInformation> GetLightningNodeInfo(string key)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (Node is null) throw new HubException("Lightning Node not available");

        var config = await Node.GetConfig();
        var peers = await Node.GetPeers();
        var chans = await Node.GetChannels() ?? [];
        var channels = chans
            .Where(channel => channel.channelDetails is not null)
            .Select(channel => channel.channelDetails)
            .OfType<ChannelDetails>()
            .ToArray();
        var bb = await _serviceProvider.GetRequiredService<OnChainWalletManager>().GetBestBlock();
        return new LightningNodeInformation
        {
            Alias = config.Alias,
            Color = config.Color,
            Version = "preprepreprealpha",
            BlockHeight = bb?.BlockHeight ?? 0,
            PeersCount = peers.Length,
            ActiveChannelsCount = channels.Count(channel => channel.get_is_usable()),
            PendingChannelsCount = channels.Count(channel => !channel.get_is_usable() && !channel.get_is_channel_ready()),
            InactiveChannelsCount = channels.Count(channel => !channel.get_is_usable() && channel.get_is_channel_ready())
        };
    }

    public async Task<LightningNodeBalance> GetLightningBalance(string key)
    {
        await AssertPermission(key, APIKeyPermission.Read);
        if (Node is null) throw new HubException("Lightning Node not available");

        var chans = await Node.GetChannels() ?? [];
        var channels = chans
            .Where(channel => channel.channelDetails is not null)
            .Select(channel => channel.channelDetails)
            .OfType<ChannelDetails>()
            .ToArray();
        var balances = Node.ClaimableBalances;
        var closing = balances
            .Where(b => b is Balance.Balance_ClaimableAwaitingConfirmations)
            .ToArray();
        return new LightningNodeBalance
        {
            OffchainBalance = new OffchainBalance
            {
                Local = LightMoney.MilliSatoshis(channels.Sum(channel => channel.get_outbound_capacity_msat())),
                Remote = LightMoney.MilliSatoshis(channels.Sum(channel => channel.get_inbound_capacity_msat())),
                Closing = LightMoney.Satoshis(closing.Sum(balance => balance.claimable_amount_satoshis()))
            }
        };
    }
}
