using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

public static class EFExtensions
{

    public static async Task<int> CrappyUpsert<T>(this DbContext ctx, T item, CancellationToken cancellationToken)
    {
        ctx.Attach(item);
        ctx.Entry(item).State = EntityState.Modified;
        try
        {
            return   await ctx.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ctx.Entry(item).State = EntityState.Added;
            return await ctx.SaveChangesAsync(cancellationToken);
        }
    }
}