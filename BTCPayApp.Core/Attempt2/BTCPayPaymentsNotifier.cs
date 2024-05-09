using BTCPayApp.CommonServer;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;

namespace BTCPayApp.Core.Attempt2;

public class BTCPayPaymentsNotifier:IScopedHostedService
{
    private readonly PaymentsManager _paymentsManager;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly OnChainWalletManager _onChainWalletManager;

    public BTCPayPaymentsNotifier(PaymentsManager paymentsManager, BTCPayConnectionManager connectionManager, OnChainWalletManager onChainWalletManager)
    {
        _paymentsManager = paymentsManager;
        _connectionManager = connectionManager;
        _onChainWalletManager = onChainWalletManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _paymentsManager.OnPaymentUpdate+= OnOnPaymentUpdate;
    }

    private async Task OnOnPaymentUpdate(object? sender, LightningPayment e)
    {
        await _connectionManager.HubProxy.SendPaymentUpdate(_onChainWalletManager.WalletConfig.Derivations[WalletDerivation.LightningScripts].Identifier, e);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _paymentsManager.OnPaymentUpdate-= OnOnPaymentUpdate;
    }
}