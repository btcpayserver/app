using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;

namespace BTCPayApp.Core.BTCPayServer;

public class BTCPayPaymentsNotifier(
    PaymentsManager paymentsManager,
    BTCPayConnectionManager connectionManager)
    : IScopedHostedService
{
    private bool _listening;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        paymentsManager.OnPaymentUpdate += OnPaymentUpdate;
        connectionManager.ConnectionChanged += ConnectionManagerOnConnectionChanged;
        return Task.CompletedTask;
    }

    private Task ConnectionManagerOnConnectionChanged(object? sender, (BTCPayConnectionState Old, BTCPayConnectionState New) e)
    {
        _listening = false;
        return Task.CompletedTask;
    }

    private async Task OnPaymentUpdate(object? sender, AppLightningPayment e)
    {
        if (!_listening || connectionManager.HubProxy is null) return;
        await connectionManager.HubProxy.SendInvoiceUpdate(e.ToInvoice());
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        paymentsManager.OnPaymentUpdate -= OnPaymentUpdate;
        return Task.CompletedTask;
    }

    public void StartListen()
    {
        _listening = true;
    }
}
