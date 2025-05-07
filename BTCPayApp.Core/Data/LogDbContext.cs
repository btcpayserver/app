using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

public class LogDbContext(DbContextOptions<LogDbContext> options) : DbContext(options)
{
    public DbSet<LogEntry> Logs { get; set; }
}
