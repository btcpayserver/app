using Comrade.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Comrade.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureComradeCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<Starter>();
        serviceCollection.AddSingleton<IHostedService, Starter>(provider => provider.GetRequiredService<Starter>());
        return serviceCollection;
    }
}






public class Starter:IHostedService
{
    private readonly IConfigProvider _configProvider;

    public TaskCompletionSource  Loaded { get; } = new();
    public Starter(IConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            Config = await _configProvider.Get<EssentialConfig>("config");
            Loaded.SetResult();
        }, cancellationToken);
        return Task.CompletedTask;
    }
    
    public async Task UpdateConfig(EssentialConfig config)
    {
        await _configProvider.Set("config", config);
        Config = config;
        
    }

    public EssentialConfig? Config { get; private set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}


public class PairSuccessResult
{
    public string Key { get; set; }
    public string StoreId { get; set; }
    public string UserId { get; set; }

    public string? ExistingWallet { get; set; }
    public string? ExistingWalletSeed { get; set; }
}
public class EssentialConfig
{
    public PairSuccessResult? PairingResult { get; set; }
    public string? PairingInstanceUri { get; set; }
    
}