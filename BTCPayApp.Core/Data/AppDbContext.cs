using System.ComponentModel.DataAnnotations;
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
    public DbSet<LightningPayment> LightningPayments { get; set; }
    // public DbSet<SpendableCoin> SpendableCoins { get; set; }




    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //TODO: add paymentId to the primary key and generate a random one if not provided
        modelBuilder.Entity<LightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound});
        base.OnModelCreating(modelBuilder);
    }
}

public class SpendableCoin
{
    public string Script { get; set; }
    [Key]
    public string Outpoint { get; set; }
    public byte[] Data { get; set; }
}