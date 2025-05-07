using BTCPayApp.Core.JsonConverters;
using BTCPayServer.Lightning;
using Laraue.EfCoreTriggers.Common.Extensions;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayApp.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Channel> LightningChannels { get; set; }
    public DbSet<ChannelAlias> ChannelAliases { get; set; }
    public DbSet<AppLightningPayment> LightningPayments { get; set; }
    public DbSet<Outbox> OutboxItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Outbox>()
            .HasKey(w => new {w.Entity, w.Key, w.ActionType, w.Version});
        modelBuilder.Entity<Outbox>().Property(payment => payment.Timestamp).HasDefaultValueSql("datetime('now')");

        modelBuilder.Entity<AppLightningPayment>().HasIndex(payment => payment.EntityKey).IsUnique();
        modelBuilder.Entity<Setting>().HasIndex(payment => payment.EntityKey).IsUnique();
        modelBuilder.Entity<Channel>().HasIndex(payment => payment.EntityKey).IsUnique();
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentRequest)
            .HasConversion(
                request => request!.ToString(),
                str => NetworkHelper.Try(network => BOLT11PaymentRequest.Parse(str, network)));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Secret)
            .HasConversion(
                request => request!.ToString(),
                str => uint256.Parse(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentHash)
            .HasConversion(
                request => request!.ToString(),
                str => uint256.Parse(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Value)
            .HasConversion(
                request => request!.MilliSatoshi,
                str => new LightMoney(str));

        modelBuilder.Entity<Channel>().Property(channel => channel.AdditionalData).HasJsonConversion();
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.AdditionalData).HasJsonConversion();
        modelBuilder.Entity<AppLightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound, w.PaymentId});


        //handling versioned data

        //settings, channels, payments

        //when creating, set the version to 0
        //when updating, increment the version

        // outbox creation
        // when creating, insert an outbox item
        // when updating, insert an outbox item
        // when deleting, insert an outbox item

        modelBuilder.Entity<Setting>()
            .AfterInsert(trigger => trigger
                .Action(group =>
                {
                    group
                        .Condition(@ref => @ref.New.Backup)
                        .Insert(
                            // .InsertIfNotExists( (@ref, outbox) => outbox.Version == @ref.New.Version && outbox.ActionType == OutboxAction.Insert && outbox.Entity == "Setting" && outbox.Key == @ref.New.Key,
                            @ref => new Outbox
                            {
                                Entity = "Setting",
                                Version = @ref.New.Version,
                                Key = @ref.New.EntityKey,
                                ActionType = OutboxAction.Insert
                            });
                }))
            .AfterDelete(trigger => trigger
                .Action(group => group
                    .Condition(@ref => @ref.Old.Backup)
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => @ref.Old.Version == outbox.Version && outbox.ActionType == OutboxAction.Delete && outbox.Entity == "Setting" && outbox.Key == @ref.Old.Key,
                        @ref => new Outbox
                        {
                            Entity = "Setting",
                            Version = @ref.Old.Version,
                            Key = @ref.Old.EntityKey,
                            ActionType = OutboxAction.Delete
                        })))
            .AfterUpdate(trigger => trigger
                .Action(group => group
                    .Condition(@ref => @ref.Old.Backup)
                    // .Condition(@ref => @ref.Old.Value != @ref.New.Value)
                    .Update<Setting>(
                        (tableRefs, setting) => tableRefs.Old.Key == setting.Key,
                        (tableRefs, setting) => new Setting { Key = tableRefs.Old.Key, Version = tableRefs.Old.Version + 1 })
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => @ref.New.Version == outbox.Version && outbox.ActionType == OutboxAction.Update && outbox.Entity == "Setting" && outbox.Key == @ref.New.Key,
                        @ref => new Outbox
                        {
                            Entity = "Setting",
                            Version = @ref.Old.Version + 1,
                            Key = @ref.New.EntityKey,
                            ActionType = OutboxAction.Update
                        })));
                // .Action(group => group
                //     .Condition(@ref => @ref.Old.Backup && !@ref.New.Backup)
                //     .Insert(
                //         // .InsertIfNotExists( (@ref, outbox) => @ref.New.Version == outbox.Version && outbox.ActionType == OutboxAction.Update && outbox.Entity == "Setting" && outbox.Key == @ref.New.Key,
                //         @ref => new Outbox()
                //         {
                //             Entity = "Setting",
                //             Version = @ref.Old.Version +1,
                //             Key = @ref.New.Key,
                //             ActionType = OutboxAction.Delete
                //         })));

        modelBuilder.Entity<Channel>()
            .AfterInsert(trigger => trigger
                .Action(group => group
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => outbox.Version == @ref.New.Version && outbox.ActionType == OutboxAction.Insert && outbox.Entity == "Channel" && outbox.Key == @ref.New.Id,
                        @ref => new Outbox
                        {
                            Entity = "Channel",
                            Version = @ref.New.Version,
                            Key = @ref.New.EntityKey,
                            ActionType = OutboxAction.Insert
                        })))
            .AfterDelete(trigger => trigger
                .Action(group => group
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => @ref.Old.Version == outbox.Version && outbox.ActionType == OutboxAction.Delete && outbox.Entity == "Channel" && outbox.Key == @ref.Old.Id,
                        @ref => new Outbox
                        {
                            Entity = "Channel",
                            Version = @ref.Old.Version,
                            Key = @ref.Old.EntityKey,
                            ActionType = OutboxAction.Delete
                        })))
            .AfterUpdate(trigger => trigger
                .Action(group => group.Update<Channel>(
                    (tableRefs, setting) => tableRefs.Old.Id == setting.Id,
                    (tableRefs, setting) => new Channel { Id = tableRefs.Old.Id, Version = tableRefs.Old.Version + 1 }).Insert(
                        // .InsertIfNotExists( (@ref, outbox) => @ref.New.Version == outbox.Version && outbox.ActionType == OutboxAction.Update && outbox.Entity == "Channel" && outbox.Key == @ref.New.Id,
                        @ref => new Outbox
                        {
                            Entity = "Channel",
                            Version = @ref.Old.Version +1,
                            Key = @ref.New.EntityKey,
                            ActionType = OutboxAction.Update
                        })));

        modelBuilder.Entity<AppLightningPayment>()
            .AfterInsert(trigger => trigger
                .Action(group => group
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => outbox.Version == @ref.New.Version && outbox.ActionType == OutboxAction.Insert && outbox.Entity == "Payment" && outbox.Key == @ref.New.PaymentHash+ "_"+@ref.New.PaymentId+ "_"+@ref.New.Inbound,
                        @ref => new Outbox
                        {
                            Entity = "Payment",
                            Version = @ref.New.Version,
                            Key = @ref.New.EntityKey,
                            ActionType = OutboxAction.Insert
                        })))
            .AfterDelete(trigger => trigger
                .Action(group => group
                    .Insert(
                        // .InsertIfNotExists( (@ref, outbox) => @ref.Old.Version == outbox.Version && outbox.ActionType == OutboxAction.Delete && outbox.Entity == "Payment" && outbox.Key == @ref.Old.PaymentHash+ "_"+@ref.Old.PaymentId+ "_"+@ref.Old.Inbound,
                        @ref => new Outbox
                        {
                            Entity = "Payment",
                            Version = @ref.Old.Version,
                            Key = @ref.Old.EntityKey,
                            ActionType = OutboxAction.Delete
                        })))
            .AfterUpdate(trigger => trigger
                .Action(group =>

                    group.Update<AppLightningPayment>(
                    (tableRefs, setting) => tableRefs.Old.PaymentHash == setting.PaymentHash,
                    (tableRefs, setting) => new AppLightningPayment {Version = tableRefs.Old.Version + 1}).Insert(
                        // .InsertIfNotExists( (@ref, outbox) =>
                        // outbox.Version != @ref.New.Version || outbox.ActionType != OutboxAction.Update || outbox.Entity != "Payment" || outbox.Key != @ref.New.PaymentHash+ "_"+@ref.New.PaymentId+ "_"+@ref.New.Inbound,
                        @ref => new Outbox
                        {
                            Entity = "Payment",
                            Version = @ref.Old.Version +1,
                            Key = @ref.New.EntityKey,
                            ActionType = OutboxAction.Update
                        })));
        base.OnModelCreating(modelBuilder);
    }
}
