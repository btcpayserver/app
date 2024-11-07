using System.Text.Json.Serialization;
using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core;
using BTCPayServer.Client.Models;
using Fluxor;
using Microsoft.JSInterop;

namespace BTCPayApp.UI.Features;

public static class Themes
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";
}

public static class CurrencyUnit
{
    public const string SATS = "SATS";
    public const string BTC = "BTC";
}

[FeatureState]
public record UIState
{
    public string SelectedTheme { get; set; } = Themes.System;
    public string SystemTheme { get; set; } = Themes.Light;
    public string BitcoinUnit { get; set; } = CurrencyUnit.SATS;
    public HistogramType HistogramType { get; set; } = HistogramType.Week;

    [JsonIgnore]
    public RemoteData<AppInstanceInfo>? Instance;

    public record ApplyUserTheme(string Theme);
    public record SetUserTheme(string Theme);
    public record FetchInstanceInfo(string? Url);
    public record SetInstanceInfo(AppInstanceInfo? Instance, string? Error);
    public record ToggleBitcoinUnit(string? BitcoinUnit = null);
    public record SetHistogramType(HistogramType Type);

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

    public bool IsDarkMode => SelectedTheme == Themes.System? SystemTheme == Themes.Dark : SelectedTheme == Themes.Dark;

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

    protected class ToggleBitcoinUnitReducer : Reducer<UIState, ToggleBitcoinUnit>
    {
        public override UIState Reduce(UIState state, ToggleBitcoinUnit action)
        {
            var unit = action.BitcoinUnit ?? (state.BitcoinUnit == CurrencyUnit.SATS
                ? CurrencyUnit.BTC
                : CurrencyUnit.SATS);
            return state with
            {
                BitcoinUnit = unit
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

    public class UIEffects(IJSRuntime jsRuntime, IHttpClientFactory httpClientFactory, IState<UIState> state)
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
                    ? await new BTCPayAppClient(action.Url, httpClientFactory.CreateClient()).GetInstanceInfo()
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
        public async Task ToggleBitcoinUnitEffect(ToggleBitcoinUnit action, IDispatcher dispatcher)
        {
            await jsRuntime.InvokeVoidAsync("Interop.setBitcoinUnit", state.Value.BitcoinUnit);
        }

        [EffectMethod]
        public Task SetHistogramTypeEffect(SetHistogramType action, IDispatcher dispatcher)
        {
            dispatcher.Dispatch(new StoreState.SetHistogramType(action.Type));
            return Task.CompletedTask;
        }
    }
}
