using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core.Auth;
using BTCPayServer.Client.Models;
using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record StoreState
{
    public AppUserStoreInfo? StoreInfo;
    public RemoteData<PointOfSaleAppData>? PointOfSale;
    public RemoteData<IEnumerable<StoreRateResult>>? Rates;
    public RemoteData<IEnumerable<InvoiceData>>? Invoices;
    private IDictionary<string,RemoteData<InvoiceData>?> _invoicesById = new Dictionary<string, RemoteData<InvoiceData>?>();
    private IDictionary<string,RemoteData<InvoicePaymentMethodDataModel[]>?> _invoicePaymentMethodsById = new Dictionary<string, RemoteData<InvoicePaymentMethodDataModel[]>?>();

    private static string[] RateFetchExcludes = ["BTC", "SATS"];

    public record SetStoreInfo(AppUserStoreInfo? StoreInfo);
    public record FetchInvoices(string StoreId);
    protected record FetchedInvoices(IEnumerable<InvoiceData>? Invoices, string? Error);
    public record FetchInvoice(string StoreId, string InvoiceId);
    protected record FetchedInvoice(InvoiceData? Invoice, string? Error, string InvoiceId);
    public record FetchInvoicePaymentMethods(string StoreId, string InvoiceId);
    protected record FetchedInvoicePaymentMethods(InvoicePaymentMethodDataModel[]? PaymentMethods, string? Error, string InvoiceId);
    public record FetchRates(string StoreId, string? Currency);
    protected record FetchedRates(IEnumerable<StoreRateResult>? Rates, string? Error);
    public record FetchPointOfSale(string AppId);
    protected record FetchedPointOfSale(PointOfSaleAppData? AppData, string? Error);

    protected class SetStoreInfoReducer : Reducer<StoreState, SetStoreInfo>
    {
        public override StoreState Reduce(StoreState state, SetStoreInfo action)
        {
            return state with
            {
                StoreInfo = action.StoreInfo,
                PointOfSale = new RemoteData<PointOfSaleAppData>(null),
                Rates = new RemoteData<IEnumerable<StoreRateResult>>(null),
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(null)
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
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(state.Invoices?.Data, true)
            };
        }
    }

    protected class FetchedInvoicesReducer : Reducer<StoreState, FetchedInvoices>
    {
        public override StoreState Reduce(StoreState state, FetchedInvoices action)
        {
            var invoices = action.Invoices ?? state.Invoices?.Data;
            return state with
            {
                Invoices = new RemoteData<IEnumerable<InvoiceData>>(invoices, false, action.Error)
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
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, true) }
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
                    { action.InvoiceId, new RemoteData<InvoiceData>(invoice, false, action.Error) }
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
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, true) }
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
                    { action.InvoiceId, new RemoteData<InvoicePaymentMethodDataModel[]>(pms, false, action.Error) }
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
                Rates = new RemoteData<IEnumerable<StoreRateResult>>(state.Rates?.Data, true)
            };
        }
    }

    protected class FetchedRatesReducer : Reducer<StoreState, FetchedRates>
    {
        public override StoreState Reduce(StoreState state, FetchedRates action)
        {
            var rates = action.Rates ?? state.Rates?.Data;
            return state with
            {
                Rates = new RemoteData<IEnumerable<StoreRateResult>>(rates, false, action.Error)
            };
        }
    }

    protected class FetchPointOfSaleReducer : Reducer<StoreState, FetchPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchPointOfSale action)
        {
            return state with
            {
                PointOfSale = new RemoteData<PointOfSaleAppData>(state.PointOfSale?.Data, true)
            };
        }
    }

    protected class FetchedPointOfSaleReducer : Reducer<StoreState, FetchedPointOfSale>
    {
        public override StoreState Reduce(StoreState state, FetchedPointOfSale action)
        {
            var appData = action.AppData ?? state.PointOfSale?.Data;
            return state with
            {
                PointOfSale = new RemoteData<PointOfSaleAppData>(appData, false, action.Error)
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
                dispatcher.Dispatch(new FetchInvoices(store.Id!));
                dispatcher.Dispatch(new FetchPointOfSale(store.PosAppId!));
                if (!RateFetchExcludes.Contains(store.DefaultCurrency))
                    dispatcher.Dispatch(new FetchRates(store.Id!, store.DefaultCurrency));
            }
            return Task.CompletedTask;
        }

        [EffectMethod]
        public async Task FetchPointOfSaleEffect(FetchPointOfSale action, IDispatcher dispatcher)
        {
            try
            {
                var appData = await accountManager.GetClient().GetPosApp(action.AppId);
                dispatcher.Dispatch(new FetchedPointOfSale(appData, null));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedPointOfSale(null, error));
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



