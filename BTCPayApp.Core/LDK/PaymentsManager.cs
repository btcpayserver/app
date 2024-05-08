using System.Security.Cryptography;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
// using BTCPayServer.Lightning;
using NBitcoin;
using org.ldk.structs;
using LightningPayment = BTCPayApp.CommonServer.LightningPayment;

namespace nldksample.LDK;

public class PaymentsManager
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    // private readonly BTCPayConnectionManager _connectionManager;
    private readonly ChannelManager _channelManager;
    private readonly Logger _logger;
    private readonly NodeSigner _nodeSigner;
    private readonly Network _network;
    // private readonly OnChainWalletManager _onChainWalletManager;

    public PaymentsManager(
        IDbContextFactory<AppDbContext> dbContextFactory,
        // BTCPayConnectionManager connectionManager,
        ChannelManager channelManager,
        Logger logger,
        NodeSigner nodeSigner,
        Network network
        // OnChainWalletManager onChainWalletManager
    )
    {
        _dbContextFactory = dbContextFactory;
        // _connectionManager = connectionManager;
        _channelManager = channelManager;
        _logger = logger;
        _nodeSigner = nodeSigner;
        _network = network;
        // _onChainWalletManager = onChainWalletManager;
    }

    public async Task<List<LightningPayment>> List(
        Func<IQueryable<LightningPayment>, IQueryable<LightningPayment?>> filter,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await filter.Invoke(context.LightningPayments.AsNoTracking().AsQueryable())
            .ToListAsync(cancellationToken: cancellationToken))!;
    }

    public async Task<LightningPayment> RequestPayment(LightMoney amount, TimeSpan expiry, uint256 descriptionHash)
    {
        var amt = amount == LightMoney.Zero ? Option_u64Z.none() : Option_u64Z.some(amount.MilliSatoshi);
        var preimage = RandomNumberGenerator.GetBytes(32);
        var paymentHash = SHA256.HashData(preimage);
        var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // var result =
        //     org.ldk.util.UtilMethods.create_invoice_from_channelmanager_and_duration_since_epoch_with_payment_hash(
        //         _channelManager, _nodeSigner, _logger,
        //         _network.GetLdkCurrency(), amt, description, epoch, (int) Math.Ceiling(expiry.TotalSeconds),
        //         paymentHash, Option_u16Z.none());


        var descHashBytes = Sha256.from_bytes(descriptionHash.ToBytes());

        var result =
            org.ldk.util.UtilMethods.create_invoice_from_channelmanager_with_description_hash_and_duration_since_epoch(
                _channelManager, _nodeSigner, _logger,
                _network.GetLdkCurrency(), amt, descHashBytes, epoch, (int) Math.Ceiling(expiry.TotalSeconds),
                Option_u16Z.none());

        if (result is Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_Err err)
        {
            throw new Exception(err.err.to_str());
        }

        var invoice = ((Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_OK) result)
            .res;

        var bolt11 = invoice.to_str();
        var lp = new LightningPayment()
        {
            Inbound = true,
            PaymentId = "default",
            Value = amount.MilliSatoshi,
            PaymentHash = Convert.ToHexString(paymentHash),
            Secret = Convert.ToHexString(invoice.payment_secret()),
            Preimage = Convert.ToHexString(preimage),
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(epoch),
            PaymentRequests = [bolt11]
        };
        await Payment(lp);

        return lp;
    }

    public async Task<LightningPayment> PayInvoice(BOLT11PaymentRequest paymentRequest,
        LightMoney? explicitAmount = null)
    {
        var id = RandomUtils.GetBytes(32);
        var invoiceStr = paymentRequest.ToString();
        var invoice =
            ((Result_Bolt11InvoiceParseOrSemanticErrorZ.Result_Bolt11InvoiceParseOrSemanticErrorZ_OK) Bolt11Invoice
                .from_str(invoiceStr)).res;
        var amt = invoice.amount_milli_satoshis() is Option_u64Z.Option_u64Z_Some amtX ? amtX.some : 0;
        amt = Math.Max(amt, explicitAmount?.MilliSatoshi ?? 0);
        var payParams =
            PaymentParameters.from_node_id(invoice.payee_pub_key(), (int) invoice.min_final_cltv_expiry_delta());
        // payParams.set_expiry_time(Option_u64Z.some(invoice.expiry_time()));

        var lastHops = invoice.route_hints();
        var payee = Payee.clear(invoice.payee_pub_key(), lastHops, invoice.features(),
            (int) invoice.min_final_cltv_expiry_delta());
        payParams.set_payee(payee);
        var routeParams = RouteParameters.from_payment_params_and_value(payParams, amt);

        var lp = new LightningPayment()
        {
            Inbound = false,
            Value = amt,
            PaymentHash = paymentRequest.PaymentHash.ToString(),
            Secret = paymentRequest.PaymentSecret.ToString(),
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            PaymentId = Convert.ToHexString(id),
            PaymentRequests = [invoiceStr]
        };
        await Payment(lp);

        var result = _channelManager.send_payment(invoice.payment_hash(),
            RecipientOnionFields.secret_only(invoice.payment_secret()),
            id, routeParams, Retry.timeout(10));

        if (result is Result_NoneRetryableSendFailureZ.Result_NoneRetryableSendFailureZ_Err err)
        {
            throw new Exception(err.err.ToString());
        }
        return lp;
    }


    public async Task Payment(LightningPayment lightningPayment, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var x = await context.LightningPayments.Upsert(lightningPayment).RunAsync(cancellationToken);
        if (x > 0)
        {
            OnPaymentUpdate?.Invoke(this, lightningPayment);
        }
    }

    public async Task PaymentUpdate(string paymentHash, bool inbound, string paymentId, bool failure,
        string? preimage, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var payment = await context.LightningPayments.SingleOrDefaultAsync(lightningPayment =>
                lightningPayment.PaymentHash == paymentHash &&
                lightningPayment.Inbound == inbound &&
                lightningPayment.PaymentId == paymentId,
            cancellationToken);
        if (payment != null)
        {
            if (failure && payment.Status == LightningPaymentStatus.Complete)
            {
                // ignore as per ldk docs that this might happen
            }
            else
            {
                payment.Status = failure ? LightningPaymentStatus.Failed : LightningPaymentStatus.Complete;
                payment.Preimage ??= preimage;
            }

            var x = await context.SaveChangesAsync(cancellationToken);
            if (x > 0)
            {
                OnPaymentUpdate?.Invoke(this, payment);
            }
        }
    }

    public AsyncEventHandler<LightningPayment>? OnPaymentUpdate { get; set; }
}