using BTCPayApp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;

namespace BTCPayApp.Core.Services;

public class LoggingService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    public static readonly LogEventLevel[] Levels = [LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error];

    public async Task<string?> GetLatestLogAsync(LogEventLevel minLevel, int count = 100)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var levels = Levels.Skip((int)minLevel).Select(l => l.ToString());
        var logs = await dbContext.Logs
            .Where(e => levels.Contains(e.Level))
            .OrderByDescending(e => e.TimeStamp)
            .Take(count)
            .ToArrayAsync();
        var lines = logs.Select(l => $"[{l.TimeStamp:yyyy-MM-dd HH:mm:ss} {l.Level[..3].ToUpperInvariant()}] {l.RenderedMessage}").ToArray();
        return logs.Length != 0 ? string.Join("\n", lines) : null;
    }
}
