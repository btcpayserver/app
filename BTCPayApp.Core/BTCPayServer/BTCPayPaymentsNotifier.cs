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
    }

    private async Task OnPaymentUpdate(object? sender, AppLightningPayment e)
    {
        await _connectionManager.HubProxy
            .SendInvoiceUpdate(e.ToInvoice());
    }

   
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _paymentsManager.OnPaymentUpdate -= OnPaymentUpdate;
    }
}