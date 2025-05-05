using System.Text.Json;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LSP.JIT;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class PaymentsManager :
    ILDKEventHandler<Event.Event_PaymentClaimable>,
    ILDKEventHandler<Event.Event_PaymentClaimed>,
    ILDKEventHandler<Event.Event_PaymentFailed>,
    ILDKEventHandler<Event.Event_PaymentSent>
{
    public const string LightningPaymentDescriptionKey = "DescriptionHash";
    public const string LightningPaymentExpiryKey = "Expiry";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    private readonly ChannelManager _channelManager;
    private readonly Logger _logger;
    private readonly NodeSigner _nodeSigner;
    private readonly Network _network;
    private readonly LDKNode _ldkNode;

    public PaymentsManager(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ChannelManager channelManager,
        Logger logger,
        NodeSigner nodeSigner,
        Network network,
        LDKNode ldkNode)
    {
        _dbContextFactory = dbContextFactory;
        _channelManager = channelManager;
        _logger = logger;
        _nodeSigner = nodeSigner;
        _network = network;
        _ldkNode = ldkNode;
    }

    public async Task<List<AppLightningPayment>> GetTransactions(CancellationToken cancellationToken = default)
    {
        return await List(payments => payments, cancellationToken);
    }

    public async Task<List<AppLightningPayment>> List(
        Func<IQueryable<AppLightningPayment>, IQueryable<AppLightningPayment?>> filter,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return (await filter.Invoke(context.LightningPayments.AsNoTracking().AsQueryable())
            .ToListAsync(cancellationToken: cancellationToken))!;
    }

    // private Bolt11Invoice CreateInvoice(long? amt, int expirySeconds, byte[] descHash)
    // {
    //     var keyMaterial = _nodeSigner.get_inbound_payment_key_material();
    //     var preimage = RandomUtils.GetBytes(32);
    //     var paymentHash = SHA256.HashData(preimage);
    //     var expandedKey = ExpandedKey.of(keyMaterial);
    //     var inboundPayment = _channelManager.create_inbound_payment_for_hash(paymentHash,
    //         amt is null ? Option_u64Z.none() : Option_u64Z.some(amt.Value), expirySeconds, Option_u16Z.none()));
    //     var paymentSecret = inboundPayment is Result_ThirtyTwoBytesNoneZ.Result_ThirtyTwoBytesNoneZ_OK ok
    //         ? ok.res
    //         : throw new Exception("Error creating inbound payment");
    //
    //     _nodeSigner.
    //     var invoice = Bolt11Invoice.from_signed(_channelManager, _nodeSigner, _logger, _network.GetLdkCurrency(),
    // }

    public async Task<AppLightningPayment> RequestPayment(LightMoney amount, TimeSpan expiry, uint256 descriptionHash)
    {
        var amt = amount == LightMoney.Zero ? Option_u64Z.none() : Option_u64Z.some(amount.MilliSatoshi);
        var now = DateTimeOffset.UtcNow;
        var epoch = now.ToUnixTimeSeconds();
        var descHashBytes = Sha256.from_bytes(descriptionHash.ToBytes());
        var lsp = await _ldkNode.GetJITLSPService();

        generateInvoice:
        JITFeeResponse? jitFeeReponse = null;
        if (lsp?.Active is true)
        {
            jitFeeReponse = await lsp.CalculateInvoiceAmount(amount, new CancellationTokenSource(5000).Token);
            if (jitFeeReponse is not null)
            {
                amt = Option_u64Z.some(jitFeeReponse.AmountToGenerateOurInvoice);
            }
            else
            {
                lsp = null;
            }
        }
        else
        {
            lsp = null;
        }

        var result = await Task.Run(() => org.ldk.util.UtilMethods.create_invoice_from_channelmanager_with_description_hash(_channelManager, amt, descHashBytes, (int) Math.Ceiling(expiry.TotalSeconds), Option_u16Z.none()));
        if (result is Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_Err err)
        {
            throw new Exception(err.err.to_str());
        }

        var originalInvoice = ((Result_Bolt11InvoiceSignOrCreationErrorZ.Result_Bolt11InvoiceSignOrCreationErrorZ_OK) result).res;
        var preimageResult = _channelManager.get_payment_preimage(originalInvoice.payment_hash(), originalInvoice.payment_secret());
        var preimage = preimageResult switch
        {
            Result_ThirtyTwoBytesAPIErrorZ.Result_ThirtyTwoBytesAPIErrorZ_Err errx => throw new Exception(errx.err.GetError()),
            Result_ThirtyTwoBytesAPIErrorZ.Result_ThirtyTwoBytesAPIErrorZ_OK ok => ok.res,
            _ => throw new Exception("Unknown error retrieving preimage")
        };

        var parsedOriginalInvoice = BOLT11PaymentRequest.Parse(originalInvoice.to_str(), _network);
        var lp = new AppLightningPayment
        {
            Inbound = true,
            PaymentId = "default",
            Value = amount.MilliSatoshi,
            PaymentHash = parsedOriginalInvoice.PaymentHash!,
            Secret = parsedOriginalInvoice.PaymentSecret!,
            Preimage = Convert.ToHexString(preimage!).ToLower(),
            Status = LightningPaymentStatus.Pending,
            Timestamp = now,
            PaymentRequest = parsedOriginalInvoice,
            AdditionalData = new Dictionary<string, JsonElement>()
            {
                [LightningPaymentDescriptionKey] = JsonSerializer.SerializeToElement(descriptionHash.ToString()),
                [LightningPaymentExpiryKey] = JsonSerializer.SerializeToElement(now.Add(expiry))
            }
        };

        if (lsp is not null)
        {
            if (!await lsp.WrapInvoice(lp, jitFeeReponse))
            {
                amt = amount == LightMoney.Zero ? Option_u64Z.none() : Option_u64Z.some(amount.MilliSatoshi);
                lsp = null;
                goto generateInvoice;
            }
        }

        await Payment(lp);

        return lp;
    }

    public async Task<AppLightningPayment> PayInvoice(BOLT11PaymentRequest paymentRequest,
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
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var inbound = await context.LightningPayments.FirstOrDefaultAsync(lightningPayment =>
            lightningPayment.PaymentHash == paymentRequest.PaymentHash && lightningPayment.Inbound);

        if (inbound is not null)
        {
            var successSelfPay = false;
            var newOutbound = new AppLightningPayment
            {
                Inbound = false,
                Value = amt,
                PaymentHash = paymentRequest.PaymentHash,
                Secret = paymentRequest.PaymentSecret,
                Status = LightningPaymentStatus.Complete,
                Timestamp = DateTimeOffset.UtcNow,
                PaymentId = Convert.ToHexString(id).ToLower(),
                PaymentRequest = paymentRequest,
                Preimage = inbound.Preimage
            };
            await context.LightningPayments.AddAsync(newOutbound);

            if (inbound.Status == LightningPaymentStatus.Pending)
            {
                inbound.Status = LightningPaymentStatus.Complete;
                newOutbound.Status = LightningPaymentStatus.Complete;
                successSelfPay = true;
            }
            else
            {
                newOutbound.Status = LightningPaymentStatus.Failed;
            }

            await context.SaveChangesAsync();
            if (successSelfPay)
            {

                OnPaymentUpdate?.Invoke(this, inbound);
            }

            OnPaymentUpdate?.Invoke(this, newOutbound);
            return newOutbound;
        }

        var outbound = new AppLightningPayment
        {
            Inbound = false,
            Value = amt,
            PaymentHash = paymentRequest.PaymentHash,
            Secret = paymentRequest.PaymentSecret,
            Status = LightningPaymentStatus.Pending,
            Timestamp = DateTimeOffset.UtcNow,
            PaymentId = Convert.ToHexString(id).ToLower(),
            PaymentRequest = paymentRequest,
        };
        await context.LightningPayments.AddAsync(outbound);
        await context.SaveChangesAsync();

        try
        {
            var pubkey = paymentRequest.GetPayeePubKey().ToBytes();
            var payParams =
                PaymentParameters.from_node_id(pubkey, paymentRequest.MinFinalCLTVExpiry);
            payParams.set_expiry_time(Option_u64Z.some(paymentRequest.ExpiryDate.ToUnixTimeSeconds()));

            var lastHops = invoice.route_hints();
            var payee = Payee.clear(pubkey, lastHops, invoice.features(), paymentRequest.MinFinalCLTVExpiry);
            payParams.set_payee(payee);
            var routeParams = RouteParameters.from_payment_params_and_value(payParams, amt);

            var result = await Task.Run(() => _channelManager.send_payment(invoice.payment_hash(),
                RecipientOnionFields.secret_only(invoice.payment_secret()),
                id, routeParams, Retry.timeout(5)));

            if (result is Result_NoneRetryableSendFailureZ.Result_NoneRetryableSendFailureZ_Err err)
            {
                outbound.Status = LightningPaymentStatus.Failed;
                outbound.AdditionalData["Error"] = JsonSerializer.SerializeToElement(err.err.ToString());
                await context.SaveChangesAsync();
            }
        }
        catch (Exception)
        {

            outbound.Status = LightningPaymentStatus.Failed;
            await context.SaveChangesAsync();
            throw;
        }

        return outbound;
    }

    public async Task Cancel(AppLightningPayment lightningPayment)
    {
        if (lightningPayment.Inbound)
        {
            await CancelInbound(lightningPayment.PaymentHash!);
        }
        else
        {
            await CancelOutbound(lightningPayment.PaymentId!);
        }
    }

    public async Task CancelInbound(uint256 paymentHash)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var payment = await context.LightningPayments.FirstOrDefaultAsync(lightningPayment =>
            lightningPayment.Status == LightningPaymentStatus.Pending && lightningPayment.Inbound &&
            lightningPayment.PaymentHash == paymentHash);
        if (payment is not null)
        {
            payment.Status = LightningPaymentStatus.Failed;
            await context.SaveChangesAsync();
        }
    }

    public async Task CancelOutbound(string paymentId)
    {
        await Task.Run(() => _channelManager.abandon_payment(Convert.FromHexString(paymentId)));

        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var payment = await context.LightningPayments.FirstOrDefaultAsync(lightningPayment =>
            lightningPayment.Status == LightningPaymentStatus.Pending &&
            !lightningPayment.Inbound && lightningPayment.PaymentId == paymentId);
        if (payment is not null)
        {
            payment.Status = LightningPaymentStatus.Failed;
            await context.SaveChangesAsync();
        }
    }


    private async Task Payment(AppLightningPayment lightningPayment, CancellationToken cancellationToken = default)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var x = await context.Upsert(lightningPayment, cancellationToken);
        if (x > 1) //we have triggers that create an outbox record everytime so we need to check for more than 1 record
        {
            OnPaymentUpdate?.Invoke(this, lightningPayment);
        }
    }

    private async Task PaymentUpdate(uint256 paymentHash, bool inbound, string paymentId, bool failure,
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

    public AsyncEventHandler<AppLightningPayment>? OnPaymentUpdate { get; set; }

    public async Task Handle(Event.Event_PaymentClaimable eventPaymentClaimable)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var paymentHash =  uint256.Parse(Convert.ToHexString(eventPaymentClaimable.payment_hash).ToLower());
        var accept = await context.LightningPayments.FirstOrDefaultAsync(payment =>
            payment.PaymentHash == paymentHash &&
            payment.Inbound && payment.Status == LightningPaymentStatus.Pending);


        var preimage = eventPaymentClaimable.purpose.GetPreimage(out _) ??
                       (accept?.Preimage is not null ? Convert.FromHexString(accept.Preimage) : null);

        if (accept is null || preimage is null)
        {
            _channelManager.fail_htlc_backwards(eventPaymentClaimable.payment_hash);
            return;
        }

        if (accept.Value == eventPaymentClaimable.amount_msat)
        {
            _channelManager.claim_funds(preimage);
            return;
        }
        if (accept.AdditionalData.TryGetValue(Flow2Jit.LightningPaymentLSPKey, out var lspDoc) &&
            lspDoc.Deserialize<string>() is { } lsp &&
            await _ldkNode.GetJITLSPService() is { } lspService && lspService.ProviderName == lsp &&
            await lspService.IsAcceptable(accept!, eventPaymentClaimable))
        {
            _channelManager.claim_funds(preimage);
        }


        _channelManager.fail_htlc_backwards(eventPaymentClaimable.payment_hash);
    }

    public async Task Handle(Event.Event_PaymentClaimed eventPaymentClaimed)
    {
        var preimage = eventPaymentClaimed.purpose.GetPreimage(out var secret);
        if (preimage is null)
        {
            return;
        }

        var paymentHash =  uint256.Parse(Convert.ToHexString(eventPaymentClaimed.payment_hash).ToLower());
        await PaymentUpdate(paymentHash, true, "default", false,
            preimage is null ? null : Convert.ToHexString(preimage).ToLower());
    }

    public async Task Handle(Event.Event_PaymentFailed eventPaymentFailed)
    {
        var paymentHash = uint256.Parse(Convert.ToHexString(((Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some)eventPaymentFailed.payment_hash).some).ToLower());
        await PaymentUpdate(paymentHash, false,
            Convert.ToHexString(eventPaymentFailed.payment_id).ToLower(), true, null);
    }

    public async Task Handle(Event.Event_PaymentSent eventPaymentSent)
    {
        var paymentHash =  uint256.Parse(Convert.ToHexString(eventPaymentSent.payment_hash).ToLower());
        await PaymentUpdate(paymentHash, false,
            Convert.ToHexString(
                ((Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some)eventPaymentSent.payment_id).some).ToLower(),
            false,
            Convert.ToHexString(eventPaymentSent.payment_preimage).ToLower());
    }
}
