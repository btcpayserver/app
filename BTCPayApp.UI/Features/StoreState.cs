using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.Auth;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record StoreState
{
    public AppUserStoreInfo? StoreInfo;
    public RemoteData<StoreData>? Store;
    public RemoteData<PointOfSaleAppData>? PointOfSale;
    public RemoteData<IEnumerable<StoreRateResult>>? Rates;
    public RemoteData<IEnumerable<InvoiceData>>? Invoices;
    public RemoteData<IEnumerable<NotificationData>>? Notifications;
    private IDictionary<string,RemoteData<InvoiceData>?> _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>();
    private IDictionary<string,RemoteData<InvoicePaymentMethodDataModel[]>?> _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>();

    private static string[] RateFetchExcludes = ["BTC", "SATS"];

    public record SetStoreInfo(AppUserStoreInfo? StoreInfo);
    public record FetchStore(string StoreId);
    public record FetchNotifications(string StoreId);
    public record FetchedNotifications(IEnumerable<NotificationData>? Notifications, string? Error);
    public record FetchInvoices(string StoreId);
    public record FetchedInvoices(IEnumerable<InvoiceData>? Invoices, string? Error);
    public record FetchInvoice(string StoreId, string InvoiceId);
    public record FetchedInvoice(InvoiceData? Invoice, string? Error, string InvoiceId);
    public record FetchInvoicePaymentMethods(string StoreId, string InvoiceId);
    public record FetchedInvoicePaymentMethods(InvoicePaymentMethodDataModel[]? PaymentMethods, string? Error, string InvoiceId);
    public record FetchRates(string StoreId, string? Currency);
    public record FetchedRates(IEnumerable<StoreRateResult>? Rates, string? Error);
    public record FetchPointOfSale(string AppId);
    public record UpdateStore(string StoreId, UpdateStoreRequest Request);
    public record SetStore(StoreData? Store, string? Error);
    public record UpdatedStore(StoreData? Store, string? Error) : SetStore(Store, Error);
    public record UpdatePointOfSale(string AppId, PointOfSaleAppRequest Request);
    public record SetPointOfSale(PointOfSaleAppData? AppData, string? Error);
    public record UpdatedPointOfSale(PointOfSaleAppData? AppData, string? Error) : SetPointOfSale(AppData, Error);

    protected class SetStoreInfoReducer : Reducer<StoreState, SetStoreInfo>
    {
        public override StoreState Reduce(StoreState state, SetStoreInfo action)
        {
            return state with
            {
                StoreInfo = action.StoreInfo,
                Store = new RemoteData<StoreData>(),
                PointOfSale = new RemoteData<PointOfSaleAppData>(),
                Rates = new RemoteData<IEnumerable<StoreRateResult>>(),
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(),
                Notifications = new RemoteData<IEnumerable<NotificationData>>()
            };
        }
    }

    protected class FetchStoreReducer : Reducer<StoreState, FetchStore>
    {
        public override StoreState Reduce(StoreState state, FetchStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class SetStoreReducer : Reducer<StoreState, SetStore>
    {
        public override StoreState Reduce(StoreState state, SetStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with {
                    Data = action.Store ?? state.Store?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    protected class UpdateStoreReducer : Reducer<StoreState, UpdateStore>
    {
        public override StoreState Reduce(StoreState state, UpdateStore action)
        {
            return state with
            {
                Store = (state.Store ?? new RemoteData<StoreData>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class FetchNotificationsReducer : Reducer<StoreState, FetchNotifications>
    {
        public override StoreState Reduce(StoreState state, FetchNotifications action)
        {
            return state with
            {
                Notifications = (state.Notifications ?? new RemoteData<IEnumerable<NotificationData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchedNotificationsReducer : Reducer<StoreState, FetchedNotifications>
    {
        public override StoreState Reduce(StoreState state, FetchedNotifications action)
        {
            return state with
            {
                Notifications = (state.Notifications ?? new RemoteData<IEnumerable<NotificationData>>()) with
                {
                    Data = action.Notifications,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    public RemoteData<InvoiceData>? GetInvoice(string invoiceId)
    {
        if (_invoicesById.TryGetValue(invoiceId, out var invoice)) return invoice;
        var invoiceData = Invoices?.Data?.FirstOrDefault(i => i.Id == invoiceId);
        return invoiceData == null ? null : new RemoteData<InvoiceData>(invoiceData);
    }

    private static RemoteData<InvoiceData>? GetInvoice(StoreState state, string invoiceId)
    {
        return state.GetInvoice(invoiceId);
    }

    public RemoteData<InvoicePaymentMethodDataModel[]>? GetInvoicePaymentMethods(string invoiceId)
    {
        return _invoicePaymentMethodsById.TryGetValue(invoiceId, out var pms) ? pms :null;
    }

    private static RemoteData<InvoicePaymentMethodDataModel[]>? GetInvoicePaymentMethods(StoreState state, string invoiceId)
    {
        return state.GetInvoicePaymentMethods(invoiceId);
    }

    protected class FetchInvoicesReducer : Reducer<StoreState, FetchInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchInvoices action)
        {
            return state with
            {
                Invoices = (state.Invoices ?? new RemoteData<IEnumerable<InvoiceData>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchedInvoicesReducer : Reducer<StoreState, FetchedInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchedInvoices action)
        {
            return state with
            {
                Invoices = (state.Invoices ?? new RemoteData<IEnumerable<InvoiceData>>()) with
                {
                    Data = action.Invoices ?? state.Invoices?.Data,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class FetchInvoiceReducer : Reducer<StoreState, FetchInvoice>
    {
        public override StoreState Reduce(StoreState state, FetchInvoice action)
        {
            var invoice = GetInvoice(state, action.InvoiceId)?.Data;
            if (state._invoicesById.ContainsKey(action.InvoiceId))
                state._invoicesById.Remove(action.InvoiceId);
            return state with
            {
                _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>(state._invoicesById)
                {
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, null, true) }
                }
            };
        }
    }

    protected class FetchedInvoiceReducer : Reducer<StoreState, FetchedInvoice>
    {
        public override StoreState Reduce(StoreState state, FetchedInvoice action)
        {
            var invoice = action.Invoice ?? GetInvoice(state, action.InvoiceId)?.Data;
            if (state._invoicesById.ContainsKey(action.InvoiceId))
                state._invoicesById.Remove(action.InvoiceId);
            return state with
            {
                _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>(state._invoicesById)
                {
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, action.Error, false) }
                }
            };
        }
    }

    protected class FetchInvoicePaymentMethodsReducer : Reducer<StoreState, FetchInvoicePaymentMethods>
    {
        public override StoreState Reduce(StoreState state, FetchInvoicePaymentMethods action)
        {
            var pms = GetInvoicePaymentMethods(state, action.InvoiceId)?.Data;
            if (state._invoicePaymentMethodsById.ContainsKey(action.InvoiceId))
                state._invoicePaymentMethodsById.Remove(action.InvoiceId);
            return state with
            {
                _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>(state._invoicePaymentMethodsById)
                {
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, null, true) }
                }
            };
        }
    }

    protected class FetchedInvoicePaymentMethodsReducer : Reducer<StoreState, FetchedInvoicePaymentMethods>
    {
        public override StoreState Reduce(StoreState state, FetchedInvoicePaymentMethods action)
        {
            var pms = action.PaymentMethods ?? GetInvoicePaymentMethods(state, action.InvoiceId)?.Data;
            if (state._invoicePaymentMethodsById.ContainsKey(action.InvoiceId))
                state._invoicePaymentMethodsById.Remove(action.InvoiceId);
            return state with
            {
                _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>(state._invoicePaymentMethodsById)
                {
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, action.Error) }
                }
            };
        }
    }

    protected class FetchRatesReducer : Reducer<StoreState, FetchRates>
    {
        public override StoreState Reduce(StoreState state, FetchRates action)
        {
            return state with
            {
                Rates = (state.Rates ?? new RemoteData<IEnumerable<StoreRateResult>>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class FetchedRatesReducer : Reducer<StoreState, FetchedRates>
    {
        public override StoreState Reduce(StoreState state, FetchedRates action)
        {
            return state with
            {
                Rates = (state.Rates ?? new RemoteData<IEnumerable<StoreRateResult>>()) with {
                    Data = action.Rates ?? state.Rates?.Data,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class FetchPointOfSaleReducer : Reducer<StoreState, FetchPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchPointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class UpdatePointOfSaleReducer : Reducer<StoreState, UpdatePointOfSale>
    {
        public override StoreState Reduce(StoreState state, UpdatePointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Sending = true
                }
            };
        }
    }

    protected class SetPointOfSaleReducer : Reducer<StoreState, SetPointOfSale>
    {
        public override StoreState Reduce(StoreState state, SetPointOfSale action)
        {
            return state with
            {
                PointOfSale = (state.PointOfSale ?? new RemoteData<PointOfSaleAppData>()) with
                {
                    Data = action.AppData ?? state.PointOfSale?.Data,
                    Error = action.Error,
                    Loading = false,
                    Sending = false
                }
            };
        }
    }

    public class StoreEffects(IAccountManager accountManager)
    {
        [EffectMethod]
        public Task SetStoreInfoEffect(SetStoreInfo action, IDispatcher dispatcher)
        {
            var store = action.StoreInfo;
            if (store != null)
            {
                var storeId = store.Id!;
                dispatcher.Dispatch(new FetchNotifications(storeId));
                dispatcher.Dispatch(new FetchInvoices(storeId));
                dispatcher.Dispatch(new FetchPointOfSale(store.PosAppId!));
                if (!RateFetchExcludes.Contains(store.DefaultCurrency))
                    dispatcher.Dispatch(new FetchRates(storeId, store.DefaultCurrency));
            }
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task UpdateStoreEffect(UpdateStore action, IDispatcher dispatcher)
        {
            try
            {
                var store = await accountManager.GetClient().UpdateStore(action.StoreId, action.Request);
                dispatcher.Dispatch(new UpdatedStore(store, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedStore(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchStoreEffect(FetchStore action, IDispatcher dispatcher)
        {
            try
            {
                var store = await accountManager.GetClient().GetStore(action.StoreId);
                dispatcher.Dispatch(new SetStore(store, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetStore(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchNotificationsEffect(FetchNotifications action, IDispatcher dispatcher)
        {
            try
            {
                var notifications = await accountManager.GetClient().GetNotifications(storeId: [action.StoreId]);
                dispatcher.Dispatch(new FetchedNotifications(notifications, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedNotifications(null, error));
            }
        }

        [EffectMethod]
        public async Task UpdatePointOfSaleEffect(UpdatePointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().UpdatePointOfSaleApp(action.AppId, action.Request);
                dispatcher.Dispatch(new UpdatedPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new UpdatedPointOfSale(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchPointOfSaleEffect(FetchPointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().GetPosApp(action.AppId);
                dispatcher.Dispatch(new SetPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetPointOfSale(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchRatesEffect(FetchRates action, IDispatcher dispatcher)
        {
            try
            {
                var rates = await accountManager.GetClient().GetStoreRates(action.StoreId, [$"BTC_{action.Currency}"]);
                dispatcher.Dispatch(new FetchedRates(rates, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedRates(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchInvoicesEffect(FetchInvoices action, IDispatcher dispatcher)
        {
            try
            {
                var invoices = await accountManager.GetClient().GetInvoices(action.StoreId);
                dispatcher.Dispatch(new FetchedInvoices(invoices, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedInvoices(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchInvoiceEffect(FetchInvoice action, IDispatcher dispatcher)
        {
            try
            {
                var invoice = await accountManager.GetClient().GetInvoice(action.StoreId, action.InvoiceId);
                dispatcher.Dispatch(new FetchedInvoice(invoice, null, action.InvoiceId));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedInvoice(null, error, action.InvoiceId));
            }
        }

        [EffectMethod]
        public async Task FetchInvoicePaymentMethodsEffect(FetchInvoicePaymentMethods action, IDispatcher dispatcher)
        {
            try
            {
                var pms = await accountManager.GetClient().GetInvoicePaymentMethods(action.StoreId, action.InvoiceId);
                dispatcher.Dispatch(new FetchedInvoicePaymentMethods(pms, null, action.InvoiceId));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedInvoicePaymentMethods(null, error, action.InvoiceId));
            }
        }
    }
}



