using Fluxor;

namespace BTCPayApp.UI.Features;

[FeatureState]
public class UIState
{
    public string SelectedTheme { get; set; } = "system";
    public string EffectiveTheme { get; set; } = "dark";
    public string SystemTheme { get; set; } = "dark";

    public record SystemPreferenceLoadedAction(string theme);
    public record ThemeSelectedAction(string theme);
    
    [ReducerMethod]
    public static UIState Reduce(UIState state, SystemPreferenceLoadedAction action)
    {
        return new UIState()
        {
            SystemTheme = action.theme,
            SelectedTheme = state.SelectedTheme,
            EffectiveTheme = state.SelectedTheme != "system" ? state.EffectiveTheme: action.theme
        };
    }
    [ReducerMethod]
    public static UIState Reduce(UIState state, ThemeSelectedAction action)
    {
        return new UIState()
        {
            SystemTheme = state.SystemTheme,
            SelectedTheme = action.theme,
            EffectiveTheme = action.theme == "system" ? state.SystemTheme: action.theme
        };
    }


}