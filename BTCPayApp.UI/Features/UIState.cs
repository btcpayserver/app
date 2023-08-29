using Fluxor;
using Microsoft.JSInterop;

namespace BTCPayApp.UI.Features;

[FeatureState]
public class UIState
{
    public string SelectedTheme { get; init; } = Constants.SystemTheme;
    private string SystemTheme { get; init; } = Constants.LightTheme;
    public bool IsDarkMode { get; set; }

    public record ApplyUserTheme(string Theme);
    public record SetSystemPreference(string Theme);
    public record SetUserTheme(string Theme);

    [ReducerMethod]
    public static UIState Reduce(UIState state, SetSystemPreference action)
    {
        var effectiveTheme = state.SelectedTheme == Constants.SystemTheme ? action.Theme : state.SelectedTheme;
        return new UIState
        {
            SystemTheme = action.Theme,
            SelectedTheme = state.SelectedTheme,
            IsDarkMode = effectiveTheme == Constants.DarkTheme
        };
    }

    [ReducerMethod]
    public static UIState Reduce(UIState state, SetUserTheme action)
    {
        var effectiveTheme = action.Theme == Constants.SystemTheme ? state.SystemTheme : action.Theme;
        return new UIState
        {
            SystemTheme = state.SystemTheme,
            SelectedTheme = action.Theme,
            IsDarkMode = effectiveTheme == Constants.DarkTheme
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
            await _jsRuntime.InvokeVoidAsync("setTheme", action.Theme);
        }
    }
}
