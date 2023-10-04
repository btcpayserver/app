using BTCPayApp.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core;

public class BTCPayAppConfigManager : IHostedService
{
    private const string PairConfigKey = "pairconfig";
    private const string WalletConfigKey = "walletconfig";

    private readonly IConfigProvider _configProvider;
    private readonly TaskCompletionSource _pairConfigLoaded = new();
    private readonly TaskCompletionSource _walletConfigLoaded = new();

    public event EventHandler<BTCPayPairConfig?>? PairConfigUpdated;
    public event EventHandler<WalletConfig?>? WalletConfigUpdated;
    public TaskCompletionSource Loaded { get; private set; } = new();
    public BTCPayPairConfig? PairConfig { get; private set; }
    public WalletConfig? WalletConfig { get; private set; }

    public BTCPayAppConfigManager(IConfigProvider configProvider)
    {
        _configProvider = configProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = LoadPairConfig();
        _ = LoadWalletConfig();

        Task.Run(async () =>
        {
            await Task.WhenAll(_pairConfigLoaded.Task, _walletConfigLoaded.Task);
            Loaded.TrySetResult();
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task LoadPairConfig()
    {
        PairConfig = await _configProvider.Get<BTCPayPairConfig>(PairConfigKey);
        PairConfigUpdated?.Invoke(this, PairConfig);
        _pairConfigLoaded.TrySetResult();
    }

    private async Task LoadWalletConfig()
    {
        WalletConfig = await _configProvider.Get<WalletConfig>(WalletConfigKey);
        WalletConfigUpdated?.Invoke(this, WalletConfig);
        _walletConfigLoaded.TrySetResult();
    }

    public async Task UpdateConfig(BTCPayPairConfig? config)
    {
        if (config == PairConfig)
            return;
        await _configProvider.Set(PairConfigKey, config);
        PairConfig = config;
        PairConfigUpdated?.Invoke(this, PairConfig);
    }

    public async Task UpdateConfig(WalletConfig? config)
    {
        if (config == WalletConfig)
            return;
        await _configProvider.Set(WalletConfigKey, config);
        WalletConfig = config;
        WalletConfigUpdated?.Invoke(this, WalletConfig);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
