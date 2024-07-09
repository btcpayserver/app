using BTCPayApp.CommonServer;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.UI.Features;
using BTCPayServer.Events;
using Fluxor;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.UI;

public class StateMiddleware(
    IConfigProvider configProvider,
    BTCPayConnectionManager btcPayConnectionManager,
    LightningNodeManager lightningNodeService,
    OnChainWalletManager onChainWalletManager,
    BTCPayAppServerClient btcpayAppServerClient,
    ILogger<StateMiddleware> Logger)
    : Middleware
{
    public const string UiStateConfigKey = "uistate";

    public override async Task InitializeAsync(IDispatcher dispatcher, IStore store)
    {
        if (store.Features.TryGetValue(typeof(UIState).FullName, out var uiStateFeature))
        {
            var existing = await configProvider.Get<UIState>(UiStateConfigKey);
            if (existing is not null)
            {
                uiStateFeature.RestoreState(existing);
            }
            uiStateFeature.StateChanged += async (sender, args) =>
            {
                var state = (UIState)uiStateFeature.GetState() with { Instance = null };
                await configProvider.Set(UiStateConfigKey, state);
            };

            store.Initialized.ContinueWith(task => ListenIn(dispatcher));
        }

        await base.InitializeAsync(dispatcher, store);

    }

    private void ListenIn(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
        dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
        dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));

        btcPayConnectionManager.ConnectionChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.ConnectionStateUpdatedAction(btcPayConnectionManager.ConnectionState));
            return Task.CompletedTask;
        };

        lightningNodeService.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.LightningNodeStateUpdatedAction(lightningNodeService.State));
            return Task.CompletedTask;
        };

        onChainWalletManager.StateChanged += (sender, args) =>
        {
            dispatcher.Dispatch(new RootState.OnChainWalletStateUpdatedAction(onChainWalletManager.State));
            return Task.CompletedTask;
        };

        btcpayAppServerClient.OnNotifyServerEvent += (sender, serverEvent) =>
        {
            Logger.LogDebug("Received Server Event: {ServerEventType}", serverEvent);
            switch (serverEvent)
            {
                case "notifications-updated":
                    dispatcher.Dispatch(new NotificationState.FetchNotifications());
                    break;
                case "invoice-updated":
                    /*var storeId = ((ServerEvent<InvoiceEvent>)serverEvent).Event?.Invoice.StoreId;
                    if (storeId != null) dispatcher.Dispatch(new StoreState.FetchInvoices(storeId));*/
                    break;
                case "store-created":
                    break;
                case "store-updated":
                    break;
                case "store-removed":
                    break;
                case "user-store-added":
                    break;
                case "user-store-updated":
                    break;
                case "user-store-removed":
                    break;
            }

            return Task.CompletedTask;
        };
    }
}
