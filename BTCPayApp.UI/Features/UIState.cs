using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public class UIState
{
    public string SelectedTheme { get; set; } = Constants.SystemTheme;
    public string SystemTheme { get; set; } = Constants.LightTheme;
    public bool IsDarkMode { get; set; } = false;

    public record ApplySystemPreference(string Theme);
    public record ApplyTheme(string Theme);

    [ReducerMethod]
    public static UIState Reduce(UIState state, ApplySystemPreference action)
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
    public static UIState Reduce(UIState state, ApplyTheme action)
    {
        var effectiveTheme = action.Theme == Constants.SystemTheme ? state.SystemTheme : action.Theme;
        return new UIState
        {
            SystemTheme = state.SystemTheme,
            SelectedTheme = action.Theme,
            IsDarkMode = effectiveTheme == Constants.DarkTheme
        };
    }
}
