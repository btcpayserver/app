using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.JsonConverters;
using BTCPayApp.Core.LDK;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NBitcoin;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }

    public DbSet<Channel> LightningChannels { get; set; }
    public DbSet<AppLightningPayment> LightningPayments { get; set; }
    // public DbSet<SpendableCoin> SpendableCoins { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentRequest)
            .HasConversion(
                request => request.ToString(), 
                str => NetworkHelper.Try(network => BOLT11PaymentRequest.Parse(str, network)));
        
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Secret)
            .HasConversion(
                request => request.ToString(), 
                str =>uint256.Parse(str));
        
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.PaymentHash)
            .HasConversion(
                request => request.ToString(), 
                str =>uint256.Parse(str));
        
        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.Value)
            .HasConversion(
                request => request.MilliSatoshi, 
                str => new LightMoney(str));

        modelBuilder.Entity<AppLightningPayment>().Property(payment => payment.AdditionalData).HasJsonConversion();
        modelBuilder.Entity<AppLightningPayment>()
            .HasKey(w => new {w.PaymentHash, w.Inbound, w.PaymentId});
        base.OnModelCreating(modelBuilder);
    }
}

public static class ValueConversionExtensions
{
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> propertyBuilder) where T : class, new()
    {
        var converter = new ValueConverter<T, string>
        (
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<T>(v, JsonSerializerOptions.Default) ?? new T()
        );

        var comparer = new ValueComparer<T>
        (
            (l, r) => JsonSerializer.Serialize(l,JsonSerializerOptions.Default) == JsonSerializer.Serialize(r,JsonSerializerOptions.Default),
            v => v == null ? 0 : JsonConvert.SerializeObject(v).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v,JsonSerializerOptions.Default), JsonSerializerOptions.Default)!
        );

        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueConverter(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        propertyBuilder.HasColumnType("jsonb");

        return propertyBuilder;
    }
}