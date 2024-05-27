using System.Security.Cryptography;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using org.ldk.structs;
// using BTCPayServer.Lightning;
using LightningPayment = BTCPayApp.CommonServer.LightningPayment;

namespace BTCPayApp.Core.LDK;

public class PaymentsManager :
    ILDKEventHandler<Event.Event_PaymentClaimable>,
    ILDKEventHandler<Event.Event_PaymentClaimed>,
    ILDKEventHandler<Event.Event_PaymentFailed>,
    ILDKEventHandler<Event.Event_PaymentSent>
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    private readonly ChannelManager _channelManager;
    private readonly Logger _logger;
    private readonly NodeSigner _nodeSigner;
    private readonly Network _network;

    public PaymentsManager(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ChannelManager channelManager,
        Logger logger,
        NodeSigner nodeSigner,
        Network network
    )
    {
        _dbContextFactory = dbContextFactory;
        _channelManager = channelManager;
        _logger = logger;
        _nodeSigner = nodeSigner;
        _network = network;
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

        //check if we have a db record with same pay hash but has the preimage set

var payHash = Convert.ToHexString(invoice.payment_hash());
var paySecret = Convert.ToHexString(invoice.payment_secret());
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var inbound = await context.LightningPayments.FirstOrDefaultAsync(lightningPayment =>
            lightningPayment.PaymentHash == payHash && lightningPayment.Inbound);

        if (inbound is not null)
        {
            var newOutbound = new LightningPayment()
            {
                Inbound = false,
                Value = amt,
                PaymentHash = payHash,
                Secret = paySecret,
                Status = LightningPaymentStatus.Complete,
                Timestamp = DateTimeOffset.UtcNow,
                PaymentId = Convert.ToHexString(id),
                PaymentRequests = [invoiceStr],
                Preimage = inbound.Preimage
            };
            await context.LightningPayments.AddAsync(newOutbound);

            if (inbound.Status == LightningPaymentStatus.Pending)
            {
                inbound.Status = LightningPaymentStatus.Complete;
                newOutbound.Status = LightningPaymentStatus.Complete;
            }
            else
            {
                newOutbound.Status = LightningPaymentStatus.Failed;
            }

            await context.SaveChangesAsync();
            return newOutbound;
        }

        var outbound = new LightningPayment()
        {
            Inbound = false,
            Value = amt,
            PaymentHash = payHash,
            Secret = paySecret,
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            PaymentId = Convert.ToHexString(id),
            PaymentRequests = [invoiceStr],
        };
        await context.LightningPayments.AddAsync(outbound);
        await context.SaveChangesAsync();
        
        var payParams =
            PaymentParameters.from_node_id(invoice.payee_pub_key(), (int) invoice.min_final_cltv_expiry_delta());
        // payParams.set_expiry_time(Option_u64Z.some(invoice.expiry_time()));

        var lastHops = invoice.route_hints();
        var payee = Payee.clear(invoice.payee_pub_key(), lastHops, invoice.features(),
            (int) invoice.min_final_cltv_expiry_delta());
        payParams.set_payee(payee);
        var routeParams = RouteParameters.from_payment_params_and_value(payParams, amt);

        var result = _channelManager.send_payment(invoice.payment_hash(),
            RecipientOnionFields.secret_only(invoice.payment_secret()),
            id, routeParams, Retry.attempts(1));

        if (result is Result_NoneRetryableSendFailureZ.Result_NoneRetryableSendFailureZ_Err err)
        {
            throw new Exception(err.err.ToString());
        }

        return outbound;
    }

    public async Task Cancel(string id, bool inbound)
    {
        if (!inbound)
        {
            _ = Task.Run(() => _channelManager.abandon_payment(Convert.FromHexString(id)) );
            
            // return;
        }
        
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var payment = await context.LightningPayments.FirstOrDefaultAsync(lightningPayment =>
            lightningPayment.Status == LightningPaymentStatus.Pending &&
            
            ((inbound && lightningPayment.Inbound && lightningPayment.PaymentHash == id) ||
             (!inbound && !lightningPayment.Inbound && lightningPayment.PaymentId == id)));
        if (payment is not null)
        {
            payment.Status = LightningPaymentStatus.Failed;
            await context.SaveChangesAsync();
        }
        
    }


    private async Task Payment(LightningPayment lightningPayment, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var x = await context.LightningPayments.Upsert(lightningPayment).RunAsync(cancellationToken);
        if (x > 0)
        {
            OnPaymentUpdate?.Invoke(this, lightningPayment);
        }
    }

    private async Task PaymentUpdate(string paymentHash, bool inbound, string paymentId, bool failure,
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

    public async Task Handle(Event.Event_PaymentClaimable eventPaymentClaimable)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var accept = await context.LightningPayments.FirstOrDefaultAsync(payment =>
            payment.PaymentHash == Convert.ToHexString(eventPaymentClaimable.payment_hash) &&
            payment.Inbound && payment.Status == LightningPaymentStatus.Pending);


        var preimage = eventPaymentClaimable.purpose.GetPreimage(out _) ??
                       (accept?.Preimage is not null ? Convert.FromHexString(accept.Preimage) : null);
        if (accept is not null && preimage is not null)
            _channelManager.claim_funds(preimage);
        else

            _channelManager.fail_htlc_backwards(eventPaymentClaimable.payment_hash);
    }

    public async Task Handle(Event.Event_PaymentClaimed eventPaymentClaimed)
    {
        var preimage = eventPaymentClaimed.purpose.GetPreimage(out var secret);
        await Payment(new LightningPayment()
        {
            PaymentId = "default",
            PaymentHash = Convert.ToHexString(eventPaymentClaimed.payment_hash),
            Inbound = true,
            Secret = secret is null ? null : Convert.ToHexString(secret),
            Timestamp = DateTimeOffset.UtcNow,
            Preimage = preimage is null ? null : Convert.ToHexString(preimage),
            Value = eventPaymentClaimed.amount_msat,
            Status = LightningPaymentStatus.Complete
        });
    }

    public async Task Handle(Event.Event_PaymentFailed @eventPaymentFailed)
    {
        await PaymentUpdate(Convert.ToHexString(eventPaymentFailed.payment_hash), false,
            Convert.ToHexString(eventPaymentFailed.payment_id), true, null);
    }

    public async Task Handle(Event.Event_PaymentSent eventPaymentSent)
    {
        await PaymentUpdate(Convert.ToHexString(eventPaymentSent.payment_hash), false,
            Convert.ToHexString(
                ((Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some) eventPaymentSent.payment_id).some), false,
            Convert.ToHexString(eventPaymentSent.payment_preimage));
    }
}