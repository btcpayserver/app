﻿// <auto-generated />
using BTCPayServer.Plugins.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Plugins.App.Migrations
{
    [DbContext(typeof(AppPluginDbContext))]
    partial class AppPluginDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("BTCPayServer.Plugins.App")
                .HasAnnotation("ProductVersion", "8.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BTCPayServer.Plugins.App.Data.Models.AppStorageItemData", b =>
                {
                    b.Property<string>("Key")
                        .HasColumnType("text");

                    b.Property<long>("Version")
                        .HasColumnType("bigint");

                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<byte[]>("Value")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.HasKey("Key", "Version", "UserId");

                    b.HasIndex("Key", "UserId")
                        .IsUnique();

                    b.ToTable("AppStorageItems", "BTCPayServer.Plugins.App", t =>
                        {
                            t.HasTrigger("LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA");
                        });

                    b.HasAnnotation("LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA", "CREATE FUNCTION \"BTCPayServer.Plugins.App\".\"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() RETURNS trigger as $LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$\r\nBEGIN\r\n  DELETE FROM \"BTCPayServer.Plugins.App\".\"AppStorageItems\"\r\n  WHERE NEW.\"UserId\" = \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"UserId\" AND NEW.\"Key\" = \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"Key\" AND NEW.\"Version\" > \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"Version\";\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA BEFORE INSERT\r\nON \"BTCPayServer.Plugins.App\".\"AppStorageItems\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"BTCPayServer.Plugins.App\".\"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"();");
                });
#pragma warning restore 612, 618
        }
    }
}
