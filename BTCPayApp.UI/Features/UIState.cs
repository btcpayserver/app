using System.Text.Json.Serialization;
using BTCPayApp.Core;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Models;
using BTCPayApp.UI.Util;
using BTCPayServer.Client.Models;
using Fluxor;
using Microsoft.JSInterop;

namespace BTCPayApp.UI.Features;

[FeatureState]
public record UIState
{
    public string SelectedTheme { get; set; } = Themes.System;
    public string SystemTheme { get; set; } = Themes.Light;
    public string DisplayCurrency { get; set; } = CurrencyDisplay.SATS;
    public string? FiatCurrency { get; set; }
    public HistogramType HistogramType { get; set; } = HistogramType.Week;

    [JsonIgnore]
    public RemoteData<AppInstanceInfo>? Instance;

    public record ApplyUserTheme(string Theme);
    public record SetUserTheme(string Theme);
    public record FetchInstanceInfo(string? Url);
    public record SetInstanceInfo(AppInstanceInfo? Instance, string? Error);
    public record ToggleDisplayCurrency(string? Currency = null);
    public record SetFiatCurrency(string? Currency = null);
    public record SetHistogramType(HistogramType Type);

    public bool IsDarkMode => SelectedTheme == Themes.System? SystemTheme == Themes.Dark : SelectedTheme == Themes.Dark;

    protected class SetUserThemeReducer : Reducer<UIState, SetUserTheme>
    {
        public override UIState Reduce(UIState state, SetUserTheme action)
        {
            return state with
            {
                SystemTheme = state.SystemTheme,
                SelectedTheme = action.Theme
            };
        }
    }

    protected class SetFiatCurrencyReducer : Reducer<UIState, SetFiatCurrency>
    {
        public override UIState Reduce(UIState state, SetFiatCurrency action)
        {
            return state with
            {
                FiatCurrency = action.Currency
            };
        }
    }

    protected class FetchInstanceInfoReducer : Reducer<UIState, FetchInstanceInfo>
    {
        public override UIState Reduce(UIState state, FetchInstanceInfo action)
        {
            return state with
            {
                Instance = (state.Instance ?? new RemoteData<AppInstanceInfo>()) with
                {
                    Loading = true
                }
            };
        }
    }

    protected class SetInstanceInfoReducer : Reducer<UIState, SetInstanceInfo>
    {
        public override UIState Reduce(UIState state, SetInstanceInfo action)
        {
            return state with
            {
                Instance = (state.Instance ?? new RemoteData<AppInstanceInfo>()) with
                {
                    Data = action.Instance,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    protected class ToggleDisplayCurrencyReducer : Reducer<UIState, ToggleDisplayCurrency>
    {
        public override UIState Reduce(UIState state, ToggleDisplayCurrency action)
        {
            var unit = action.Currency ?? state.DisplayCurrency switch
            {
                CurrencyDisplay.BTC => CurrencyDisplay.SATS,
                CurrencyDisplay.SATS => string.IsNullOrEmpty(state.FiatCurrency)
                    ? CurrencyDisplay.BTC
                    : state.FiatCurrency,
                _ => CurrencyDisplay.BTC
            };
            return state with
            {
                DisplayCurrency = unit
            };
        }
    }

    protected class SetHistogramTypeReducer : Reducer<UIState, SetHistogramType>
    {
        public override UIState Reduce(UIState state, SetHistogramType action)
        {
            return state with
            {
                HistogramType = action.Type
            };
        }
    }

    public class UIEffects(IAccountManager accountManager, IJSRuntime jsRuntime, IState<UIState> state)
    {
        [EffectMethod]
        public async Task ApplyUserThemeEffect(ApplyUserTheme action, IDispatcher dispatcher)
        {
            // store
            dispatcher.Dispatch(new SetUserTheme(action.Theme));
            // ui
            await jsRuntime.InvokeVoidAsync("Interop.setColorMode", action.Theme);
        }

        [EffectMethod]
        public async Task FetchInstanceInfoEffect(FetchInstanceInfo action, IDispatcher dispatcher)
        {
            try
            {
                var instance = !string.IsNullOrEmpty(action.Url)
                    ? await accountManager.GetClient(action.Url).GetInstanceInfo()
                    : null;

                var error = !string.IsNullOrEmpty(action.Url) && instance == null
                    ? "This server does not seem to support the BTCPay app."
                    : null;

                dispatcher.Dispatch(new SetInstanceInfo(instance, error));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new SetInstanceInfo(null, error));
            }
        }

        [EffectMethod]
        public async Task SetInstanceInfoEffect(SetInstanceInfo action, IDispatcher dispatcher)
        {
            var info = action.Instance;
            await jsRuntime.InvokeVoidAsync("Interop.setInstanceInfo", info?.CustomThemeExtension, info?.CustomThemeCssUrl, info?.LogoUrl);
        }

        [EffectMethod]
        public async Task ToggleDisplayCurrencyEffect(ToggleDisplayCurrency action, IDispatcher dispatcher)
        {
            await jsRuntime.InvokeVoidAsync("Interop.setBitcoinUnit", state.Value.DisplayCurrency);
        }

        [EffectMethod]
        public Task SetHistogramTypeEffect(SetHistogramType action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new StoreState.SetHistogramType(action.Type));
            return Task.CompletedTask;
        }
    }
}
