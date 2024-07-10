using System.ComponentModel.DataAnnotations;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.JsonConverters;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Laraue.EfCoreTriggers.Common.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NBitcoin;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }

    public DbSet<Channel> LightningChannels { get; set; }
    public DbSet<AppLightningPayment> LightningPayments { get; set; }
    public DbSet<Outbox> OutboxItems { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentRequest)
            .HasConversion(
                request => request.ToString(),
                str => NetworkHelper.Try(network => BOLT11PaymentRequest.Parse(str, network)));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Secret)
            .HasConversion(
                request => request.ToString(),
                str => uint256.Parse(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentHash)
            .HasConversion(
                request => request.ToString(),
                str => uint256.Parse(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Value)
            .HasConversion(
                request => request.MilliSatoshi,
                str => new LightMoney(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.AdditionalData).HasJsonConversion();
        modelBuilder.Entity<AppLightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound, w.PaymentId});


        //handling versioned data
        
        
        modelBuilder.Entity<Channel>()
            .AfterDelete(trigger => 
                trigger.Action(group => 
                    group.Insert<Outbox>(
            @ref => new Outbox()
            {
                Version = @ref.Old.Version,
                Key = "Channel-" + @ref.Old.Id,
                ActionType = "delete"
            })));

        modelBuilder.Entity<Setting>()
            .AfterDelete(trigger => trigger.Action(group =>
                group
                    .Condition(@ref => @ref.Old.Backup)
                    .Insert<Outbox>(
                        @ref => new Outbox()
                        {
                            Version = @ref.Old.Version,
                            Key = "Setting-" + @ref.Old.Key,
                            ActionType = "delete"
                        })));


        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if(typeof(VersionedData).IsAssignableFrom(entityType.ClrType))
            {
                var builder = modelBuilder.Entity(entityType.ClrType);
                
                builder.Property<ulong>("Version").IsConcurrencyToken().HasDefaultValue(0);
            }
        }
        
        modelBuilder.Entity<Channel>()
            .BeforeUpdate(trigger => trigger
                .Action(action => action
                    .Condition(refs => refs.Old.Id == refs.New.Id)
                    .Update<Channel>(
                        (tableRefs, entity) => tableRefs.Old.Id == entity.Id,
                        (tableRefs, oldChannel) => new Channel() {Version = oldChannel.Version + 1})
                    .Insert<Outbox>(insert => new Outbox()
                    {
                        Key = "Channel-" + insert.New.Id,
                        Version = insert.New.Version,
                        ActionType = "update",
                        Timestamp = DateTimeOffset.UtcNow
                    }))
                .Action(action => action
                    .Condition(refs => refs.Old.Id != refs.New.Id)
                    .Insert<Outbox>(insert => new Outbox()
                    {
                        Key = "Channel-" + insert.Old.Id,
                        Version = insert.Old.Version,
                        ActionType = "delete",
                        Timestamp = DateTimeOffset.UtcNow
                    })
                    .Insert<Outbox>(insert => new Outbox()
                    {
                        Key = "Channel-" + insert.New.Id,
                        Version = insert.New.Version,
                        ActionType = "update",
                        Timestamp = DateTimeOffset.UtcNow
                    })))
            .AfterInsert(trigger => trigger
                .Action(action => action
                    .Insert<Outbox>(insert => new Outbox()
                    {
                        Key = "Channel-" + insert.New.Id,
                        Version = insert.New.Version,
                        ActionType = "insert",
                        Timestamp = DateTimeOffset.UtcNow
                    }))).AfterDelete(trigger => trigger
                .Action(action => action
                    .Insert<Outbox>(insert => new Outbox()
                    {
                        Key = "Channel-" + insert.Old.Id,
                        Version = insert.Old.Version,
                        ActionType = "delete",
                        Timestamp = DateTimeOffset.UtcNow
                    })));

        base.OnModelCreating(modelBuilder);
    }
    
}




public class Outbox
{
    public DateTimeOffset Timestamp { get; set; }
    public string ActionType { get; set; }
    public string Key { get; set; }
    public ulong Version { get; set; }
}

public class OutboxProcessor : IHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public OutboxProcessor(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    private async Task ProcessOutbox(CancellationToken cancellationToken = default)
    {
        await using var db =
            new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=outbox.db").Options);
        var outbox = db.Set<Outbox>();
        var outboxItems = await outbox.ToListAsync();
        foreach (var outboxItem in outboxItems)
        {
            // Process outbox item
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}