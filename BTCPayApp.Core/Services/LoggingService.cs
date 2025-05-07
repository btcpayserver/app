using System.Text.Json;
using BTCPayApp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Events;
using Serilog.Parsing;

namespace BTCPayApp.Core.Services;

public class LoggingService(IDbContextFactory<LogDbContext> dbContextFactory)
{
    public static readonly LogEventLevel[] Levels = [LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information, LogEventLevel.Warning, LogEventLevel.Error];

    public async Task<string?> GetLatestLogAsync(LogEventLevel minLevel, int count = 100)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var levels = Levels.Skip((int)minLevel).Select(l => l.ToString());
        var parser = new MessageTemplateParser();
        var logs = await dbContext.Logs
            .Where(e => levels.Contains(e.Level))
            .OrderByDescending(e => e.TimeStamp)
            .Take(count)
            .ToArrayAsync();
        var lines = logs.Select(l =>
        {
            var level = Enum.Parse<LogEventLevel>(l.Level, true);
            var tmpl = parser.Parse(l.RenderedMessage);
            var props = JsonSerializer.Deserialize<Dictionary<string, object>>(l.Properties);
            var properties = props?.Select(p => new LogEventProperty(p.Key, new ScalarValue(p.Value))) ?? [];
            var e = new LogEvent(l.TimeStamp, level, null, tmpl, properties);
            return $"[{e.Timestamp:yyyy-MM-dd HH:mm:ss} {e.Level.ToString()[..3].ToUpperInvariant()}] {e.RenderMessage()} ({e.Properties["SourceContext"]}){Environment.NewLine}{e.Exception}";
        });
        return logs.Length != 0 ? string.Join("", lines.Reverse()) : null;
    }
}
