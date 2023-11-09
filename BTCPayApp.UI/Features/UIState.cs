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
public class UIState
{
    public string SelectedTheme { get; init; } = Themes.System;
    private string SystemTheme { get; init; } = Themes.Light;
    public bool IsDarkMode { get; set; }

    public record ApplyUserTheme(string Theme);
    public record SetUserTheme(string Theme);

    [ReducerMethod]
    public static UIState Reduce(UIState state, SetUserTheme action)
    {
        var effectiveTheme = action.Theme == Themes.System ? state.SystemTheme : action.Theme;
        return new UIState
        {
            SystemTheme = state.SystemTheme,
            SelectedTheme = action.Theme,
            IsDarkMode = effectiveTheme == Themes.Dark
        };
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
            await _jsRuntime.InvokeVoidAsync("setColorMode", action.Theme);
        }
    }
}
