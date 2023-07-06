using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core;

public class BTCPayAppConfigManager : IHostedService
{
    private readonly IConfigProvider _configProvider;
    private readonly ILogger<BTCPayAppConfigManager> _logger;

    public event EventHandler<BTCPayPairConfig?>? PairConfigUpdated;
    public event EventHandler<WalletConfig?>? WalletConfigUpdated;
    private TaskCompletionSource _pairConfigLoaded = new();
    private TaskCompletionSource _walletConfigLoaded = new();
    public TaskCompletionSource Loaded { get; private set; } = new();

    public BTCPayAppConfigManager(IConfigProvider configProvider, ILogger<BTCPayAppConfigManager> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = LoadPairConfig();
        _ = LoadWalletConfig();
        
        Task.Run(async () =>
        {
            await Task.WhenAll(_pairConfigLoaded.Task, _walletConfigLoaded.Task);
            Loaded.TrySetResult();
        });
        
        return Task.CompletedTask;
    }

    private async Task LoadPairConfig()
    {
        PairConfig = await _configProvider.Get<BTCPayPairConfig>("pairconfig");
        PairConfigUpdated?.Invoke(this, PairConfig);
        _pairConfigLoaded.TrySetResult();
    }
    private async Task LoadWalletConfig()
    {
        WalletConfig = await _configProvider.Get<WalletConfig>("walletconfig");
        WalletConfigUpdated?.Invoke(this, WalletConfig);
        _walletConfigLoaded.TrySetResult();
    }


    public async Task UpdateConfig(BTCPayPairConfig? config)
    {
        if (config == PairConfig)
            return;
        await _configProvider.Set("pairconfig", config);
        PairConfig = config;
        PairConfigUpdated?.Invoke(this, PairConfig);
    }
    public async Task UpdateConfig(WalletConfig? config)
    {
        if (config == WalletConfig)
            return;
        await _configProvider.Set("walletconfig", config);
        WalletConfig = config;
        WalletConfigUpdated?.Invoke(this, WalletConfig);
    }

    public BTCPayPairConfig? PairConfig { get; private set; }
    public WalletConfig? WalletConfig { get; private set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}