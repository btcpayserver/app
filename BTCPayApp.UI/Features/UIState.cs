using BTCPayApp.CommonServer.Models;
using Fluxor;
using Microsoft.JSInterop;

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
    string? CustomThemeExtension,
    string? CustomThemeCssUrl,
    string? LogoUrl,
    string? ServerName)
{
    public UIState() : this(Themes.System, Themes.Light, false, null, null, null, null)
    {
    }

    public record ApplyUserTheme(string Theme);
    public record SetUserTheme(string Theme);
    public record SetInstance(AppInstanceInfo? InstanceInfo);

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

    protected class SetInstanceReducer : Reducer<UIState, SetInstance>
    {
        public override UIState Reduce(UIState state, SetInstance action)
        {
            var info = action.InstanceInfo;
            return state with
            {
                LogoUrl = info?.LogoUrl,
                ServerName = info?.ServerName,
                CustomThemeCssUrl = info?.CustomThemeCssUrl,
                CustomThemeExtension = info?.CustomThemeExtension
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
        public async Task SetInstanceEffect(SetInstance action, IDispatcher dispatcher)
        {
            var info = action.InstanceInfo;
            await _jsRuntime.InvokeVoidAsync("Interop.setInstanceInfo", info?.CustomThemeExtension, info?.CustomThemeCssUrl);
        }
    }
}
