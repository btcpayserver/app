using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using org.ldk.structs;

namespace nldksample.LDK;

public class LDKPaymentEventsHandler :
    ILDKEventHandler<Event.Event_PaymentClaimable>,
    ILDKEventHandler<Event.Event_PaymentClaimed>,
    ILDKEventHandler<Event.Event_PaymentFailed>,
    ILDKEventHandler<Event.Event_PaymentSent>
{
    private readonly CurrentWalletService _currentWalletService;
    private readonly ChannelManager _channelManager;

    public LDKPaymentEventsHandler(CurrentWalletService currentWalletService, ChannelManager channelManager)
    {
        _currentWalletService = currentWalletService;
        _channelManager = channelManager;
    }

    public async Task Handle(Event.Event_PaymentClaimable eventPaymentClaimable)
    {
        var preimage = eventPaymentClaimable.purpose.GetPreimage(out _);
        if (preimage is not null)
            _channelManager.claim_funds(preimage);
        else

            _channelManager.fail_htlc_backwards(eventPaymentClaimable.payment_hash);
    }

    public async Task Handle(Event.Event_PaymentClaimed eventPaymentClaimed)
    {
        var preimage = eventPaymentClaimed.purpose.GetPreimage(out var secret);
        await _currentWalletService.Payment(new LightningPaymentRecord()
        {
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
        
        await _currentWalletService.PaymentUpdate(Convert.ToHexString(eventPaymentFailed.payment_hash), false,
            Convert.ToHexString(eventPaymentFailed.payment_id), true, null);
    }

    public async Task Handle(Event.Event_PaymentSent eventPaymentSent)
    {
        await _currentWalletService.PaymentUpdate(Convert.ToHexString(eventPaymentSent.payment_hash), false,
            Convert.ToHexString(
                ((Option_ThirtyTwoBytesZ.Option_ThirtyTwoBytesZ_Some) eventPaymentSent.payment_id).some), false,
            Convert.ToHexString(eventPaymentSent.payment_preimage));
    }
}