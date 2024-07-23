﻿using Laraue.EfCoreTriggers.SqlLite.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayApp.Core.Data;

public class DesignTimeAppContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=fake.db");
        optionsBuilder.UseSqlLiteTriggers();

        return new AppDbContext(optionsBuilder.Options);
    }
}