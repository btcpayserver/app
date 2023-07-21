using MudBlazor;

namespace BTCPayApp.UI;

public static class Constants
{
    public static readonly MudTheme Theme = new ()
    {
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Open Sans", "Helvetica Neue", "Arial", "sans-serif" }
            }
        },
        Palette = new PaletteLight
        {
            Primary = Colors.Green.Default,
            Secondary = Colors.Green.Accent4,
        },
        PaletteDark = new PaletteDark
        {
            Primary = Colors.Blue.Lighten1
        }
    };
}