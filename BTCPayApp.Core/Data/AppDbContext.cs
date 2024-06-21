using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BTCPayApp.CommonServer.Models;
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
            .HasKey(w => new {w.PaymentHash, w.Inbound, w.PaymentId});
//we use system.text.json because it is natively supported in efcore for querying
        modelBuilder.Entity<LightningPayment>().Property(p => p.AdditionalData)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, JsonDocument>>(v, JsonSerializerOptions.Default)!);
        base.OnModelCreating(modelBuilder);
    }
}

public class SpendableCoin
{
    public string Script { get; set; }
    [Key] public string Outpoint { get; set; }
    public byte[] Data { get; set; }
}