using System.Data;
using BTCPayApp.CommonServer;
using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }

    public DbSet<Channel> LightningChannels { get; set; }

//     public DbSet<OnchainCoin> OnchainCoins { get; set; }
//     public DbSet<OnchainScript> OnchainScripts { get; set; }
//     public List<OnChainTransaction> OnChainTransactions { get; set; }
    public DbSet<LightningPayment> LightningPayments { get; set; }
// }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //TODO: add paymentId to the primary key and generate a random one if not provided
        modelBuilder.Entity<LightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound});
        base.OnModelCreating(modelBuilder);
    }
}