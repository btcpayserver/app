using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Desktop;
using Laraue.EfCoreTriggers.SqlLite.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plugin.Fingerprint.Abstractions;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public class HeadlessTestNode : IDisposable
{
    public HeadlessTestNode(ITestOutputHelper testOutputHelper, string[]? args = null)
    {
        var hostBuilder = Host.CreateDefaultBuilder();

        hostBuilder
            .ConfigureLogging(builder =>
            {
                // check if scopes are used in normal operation
                var useScopes = builder.UsesScopes();
                // remove other logging providers, such as remote loggers or unnecessary event logs
                builder.ClearProviders();
                builder.AddConsole();
                builder.AddDebug();
                builder.Services.AddSingleton<ILoggerProvider>(_ =>
                    new WebApplicationFactoryExtensions.XunitLoggerProvider(testOutputHelper, useScopes));
            })
            .ConfigureAppConfiguration(builder =>
                builder.AddEnvironmentVariables()
                    .AddInMemoryCollection(new KeyValuePair<string, string?>[]
                    {
                        new("DB_CONNECTIONSTRING", "Data Source=:memory:")
                    }));
        hostBuilder.ConfigureServices(collection =>
        {
            collection.ConfigureBTCPayAppCore();


            collection.Replace(ServiceDescriptor.Singleton<IDbContextFactory<AppDbContext>, TestDbContextFactory>());
            collection.AddDataProtection(options => { options.ApplicationDiscriminator = "BTCPayApp"; });
            collection.AddSingleton<ISecureConfigProvider, TestSecureConfigProvider>();
            collection.AddSingleton<IFingerprint, StubFingerprintProvider>();
        });
        App = hostBuilder.Build();
    }

    public IHost App { get; }

    public void Dispose()
    {
        App.Dispose();
    }

    public class TestAppDbCOntext : AppDbContext
    {
        public TestAppDbCOntext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public override void Dispose()
        {
            // Do nothing
        }

        public override ValueTask DisposeAsync()
        {
            return new ValueTask();
        }
    }

    public class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private AppDbContext? _dbContext;

        public AppDbContext? CreateDbContext()
        {
            // if (_dbContext is not null)
            //     return _dbContext;
            var dbConnection = new SqliteConnection("Data Source=file::memory:?cache=shared");
            // dbConnection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(dbConnection)
                .UseSqlLiteTriggers()
                .Options;
            _dbContext = new TestAppDbCOntext(options);
            _dbContext.Database.EnsureCreated();
            return _dbContext;
        }
    }
}