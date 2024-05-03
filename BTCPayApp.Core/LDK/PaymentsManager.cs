using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
// using BTCPayServer.Lightning;
using NBitcoin;
using org.ldk.structs;
using LightningPayment = BTCPayApp.Core.Data.LightningPayment;

namespace nldksample.LDK;

public class PaymentsManager
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly LDKNode _ldkNode;
    private readonly ChannelManager _channelManager;
    private readonly Logger _logger;
    private readonly NodeSigner _nodeSigner;
    private readonly Network _network;

    public PaymentsManager(
        IDbContextFactory<AppDbContext> dbContextFactory,
        LDKNode ldkNode,
        ChannelManager channelManager,
        Logger logger,
        NodeSigner nodeSigner,
        Network network)
    {
        _dbContextFactory = dbContextFactory;
        _ldkNode = ldkNode;
        _channelManager = channelManager;
        _logger = logger;
        _nodeSigner = nodeSigner;
        _network = network;
    }

    public async Task<List<LightningPayment>> List(Func<IQueryable<LightningPayment>, IQueryable<LightningPayment>> filter, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await filter.Invoke(context.LightningPayments.AsNoTracking().AsQueryable()).ToListAsync(cancellationToken: cancellationToken);
    }
    
    public async Task<BOLT11PaymentRequest> RequestPayment(LightMoney amount, TimeSpan expiry, string description)
    {
        var amt = amount == LightMoney.Zero ? Option_u64Z.none() : Option_u64Z.some(amount.MilliSatoshi);
        var preimage = RandomNumberGenerator.GetBytes(32);
        var paymentHash = SHA256.HashData(preimage);
        var epoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result =
            org.ldk.util.UtilMethods.create_invoice_from_channelmanager_and_duration_since_epoch_with_payment_hash(
                _channelManager, _nodeSigner, _logger,
                _network.GetLdkCurrency(), amt, description, epoch, (int) Math.Ceiling(expiry.TotalSeconds),
                paymentHash, Option_u16Z.none());
        //  var invoice = org.ldk.util.UtilMethods.create_invoice_from_channelmanager(_channelManager, _nodeSigner, _logger,
        //     _network.GetLdkCurrency(), amt, description, (int) Math.Ceiling(expiry.TotalSeconds),Option_u16Z.none());

        if (result is Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_Err err)
        {
            throw new Exception(err.err.to_str());
        }

        var invoice = ((Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_OK) result)
            .res;

        var bolt11 = invoice.to_str();
        var paymentRequest = BOLT11PaymentRequest.Parse(bolt11, _network);
        await Payment(new LightningPayment()
        {
            Inbound = true,
            Value = amount,
            PaymentHash = Convert.ToHexString(paymentHash),
            Secret = Convert.ToHexString(invoice.payment_secret()),
            Preimage = Convert.ToHexString(preimage),
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow
        });
        return paymentRequest;
    }

    public async Task PayInvoice(BOLT11PaymentRequest paymentRequest, LightMoney? explicitAmount = null)
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

        await Payment(new LightningPayment()
        {
            Inbound = false,
            Value = amt,
            PaymentHash = paymentRequest.PaymentHash.ToString(),
            Secret = paymentRequest.PaymentSecret.ToString(),
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            PaymentId = Convert.ToHexString(id)
        });

        var result = _channelManager.send_payment(invoice.payment_hash(),
            RecipientOnionFields.secret_only(invoice.payment_secret()),
            id, routeParams, Retry.timeout(10));

        if (result is Result_NoneRetryableSendFailureZ.Result_NoneRetryableSendFailureZ_Err err)
        {
            throw new Exception(err.err.ToString());
        }
    }
    
    public async Task Payment(LightningPayment lightningPayment, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await context.LightningPayments.Upsert(lightningPayment).RunAsync(cancellationToken);
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
            await context.SaveChangesAsync(cancellationToken);
        }

    }
}