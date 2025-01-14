using Laraue.EfCoreTriggers.Common.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.App.Data.Models;

public class AppStorageItemData
{
    public string Key { get; set; }
    public long Version { get; set; }
    public byte[] Value { get; set; }
    public string UserId { get; set; }

    // TODO: Port user relation
    // public ApplicationUser User { get; set; }

    public static void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AppStorageItemData>()
            .HasKey(o => new {o.Key, o.Version, o.UserId});
        builder.Entity<AppStorageItemData>()
            .HasIndex(o => new {o.Key, o.UserId}).IsUnique();

        /* TODO: Port deletion of dependent items
         builder.Entity<ApplicationUser>()
            .HasMany(user => user.AppStorageItems)
            .WithOne(data => data.User)
            .OnDelete(DeleteBehavior.Cascade);*/

        builder.Entity<AppStorageItemData>()
            .BeforeInsert(trigger => trigger
                .Action(group => group
                    .Delete<AppStorageItemData>((@ref, entity) => @ref.New.UserId == entity.UserId && @ref.New.Key == entity.Key &&
                                                                  @ref.New.Version > entity.Version)));
    }
}
