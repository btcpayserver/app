using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;

namespace BTCPayApp.Core.BTCPayServer;

public class BTCPayPaymentsNotifier : IScopedHostedService
{
    private readonly PaymentsManager _paymentsManager;
    private readonly BTCPayConnectionManager _connectionManager;

    public BTCPayPaymentsNotifier(
        PaymentsManager paymentsManager, BTCPayConnectionManager connectionManager)
    {
        _paymentsManager = paymentsManager;
        _connectionManager = connectionManager;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _paymentsManager.OnPaymentUpdate += OnPaymentUpdate;
        _connectionManager.ConnectionChanged += ConnectionManagerOnConnectionChanged;
    }
    private bool _listening = false;

    private Task ConnectionManagerOnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        _listening = false;
        return Task.CompletedTask;
    }

    private async Task OnPaymentUpdate(object? sender, AppLightningPayment e)
    {
        if (!_listening)
            return;
        await _connectionManager.HubProxy
            .SendInvoiceUpdate(e.ToInvoice());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _paymentsManager.OnPaymentUpdate -= OnPaymentUpdate;
    }

    public void StartListen()
    {
        _listening = true;

    }
}
