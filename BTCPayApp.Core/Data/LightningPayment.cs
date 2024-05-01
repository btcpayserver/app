using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

public class LightningPayment
{
    public string PaymentHash { get; set; }
    public string? PaymentId { get; set; }
    public string? Preimage { get; set; }
    public string? Secret { get; set; }
    public bool Inbound { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public long Value { get; set; }
    public LightningPaymentStatus Status { get; set; }


    public static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound});
    }
}