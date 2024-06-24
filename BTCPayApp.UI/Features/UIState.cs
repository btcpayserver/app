using BTCPayApp.CommonServer.Models;
using BTCPayApp.Core;
using Fluxor;
using Microsoft.JSInterop;
using Newtonsoft.Json;

namespace BTCPayApp.UI.Features;

public static class Themes
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";
}

[FeatureState]
public record UIState(
    string SelectedTheme,
    string SystemTheme,
    bool IsDarkMode,
    [property: JsonIgnore] RemoteData<AppInstanceInfo>? Instance)
{
    public UIState() : this(Themes.System, Themes.Light, false, null)
    {
    }

    public record ApplyUserTheme(string Theme);
    public record SetUserTheme(string Theme);

    protected class SetUserThemeReducer : Reducer<UIState, SetUserTheme>
    {
        public override UIState Reduce(UIState state, SetUserTheme action)
        {
            var effectiveTheme = action.Theme == Themes.System ? state.SystemTheme : action.Theme;
            return state with
            {
                SystemTheme = state.SystemTheme,
                SelectedTheme = action.Theme,
                IsDarkMode = effectiveTheme == Themes.Dark
            };
        }
    }

    public record FetchInstanceInfo(string? Url);

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

    public record FetchedInstanceInfo(AppInstanceInfo? Instance, string? Error);

    protected class FetchedInstanceInfoReducer : Reducer<UIState, FetchedInstanceInfo>
    {
        public override UIState Reduce(UIState state, FetchedInstanceInfo action)
        {
            return state with
            {
                Instance = (state.Instance ?? new RemoteData<AppInstanceInfo>()) with
                {
                    Data = action.Instance ?? state.Instance?.Data,
                    Error = action.Error,
                    Loading = false
                }
            };
        }
    }

    public class UIEffects
    {
        private readonly IJSRuntime _jsRuntime;

        public UIEffects(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        [EffectMethod]
        public async Task ApplyUserThemeEffect(ApplyUserTheme action, IDispatcher dispatcher)
        {
            // store
            dispatcher.Dispatch(new SetUserTheme(action.Theme));
            // ui
            await _jsRuntime.InvokeVoidAsync("Interop.setColorMode", action.Theme);
        }

        [EffectMethod]
        public async Task FetchInstanceInfoEffect(FetchInstanceInfo action, IDispatcher dispatcher)
        {
            try
            {
                var instance = !string.IsNullOrEmpty(action.Url)
                    ? await new BTCPayAppClient(action.Url).GetInstanceInfo()
                    : null;

                var error = !string.IsNullOrEmpty(action.Url) && instance == null
                    ? "This server does not seem to support the BTCPay app."
                    : null;

                dispatcher.Dispatch(new FetchedInstanceInfo(instance, error));
            }
            catch (Exception e)
            {
                var error = e.InnerException?.Message ?? e.Message;
                dispatcher.Dispatch(new FetchedInstanceInfo(null, error));
            }
        }

        [EffectMethod]
        public async Task FetchedInstanceInfoEffect(FetchedInstanceInfo action, IDispatcher dispatcher)
        {
            var info = action.Instance;
            await _jsRuntime.InvokeVoidAsync("Interop.setInstanceInfo", info?.CustomThemeExtension, info?.CustomThemeCssUrl);
        }
    }
}
