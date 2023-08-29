using MudBlazor;
using MudBlazor.Utilities;

namespace BTCPayApp.UI;

public static class Constants
{
    public const string DarkTheme = "dark";
    public const string LightTheme = "light";
    public const string SystemTheme = "system";

    public static class Colors
    {
        public static class Brand
        {
            public static MudColor Primary { get; } = "#51B13E";
            public static MudColor Secondary { get; } = "#CEDC21";
            public static string Tertiary { get; } = "#1E7A44";
            public static MudColor Dark { get; } = "#0F3B21";
        }

        public static string Transparent { get; } = "transparent";
        public static MudColor White { get; } = "#FFFFFF";
        public static MudColor Black { get; } = "#000000";

        public static MudColor Light100 { get; } = "#F8F9FA";
        public static MudColor Light200 { get; } = "#E9ECEF";
        public static MudColor Light300 { get; } = "#DEE2E6";
        public static MudColor Light400 { get; } = "#CED4DA";
        public static MudColor Light500 { get; } = "#8F979E";
        public static MudColor Light600 { get; } = "#6C757D";
        public static MudColor Light700 { get; } = "#495057";
        public static MudColor Light800 { get; } = "#343A40";
        public static MudColor Light900 { get; } = "#292929";

        public static MudColor Dark100 { get; } = "#F0F6FC";
        public static MudColor Dark200 { get; } = "#C9D1D9";
        public static MudColor Dark300 { get; } = "#B1BAC4";
        public static MudColor Dark400 { get; } = "#8B949E";
        public static MudColor Dark500 { get; } = "#6E7681";
        public static MudColor Dark600 { get; } = "#484F58";
        public static MudColor Dark700 { get; } = "#30363D";
        public static MudColor Dark800 { get; } = "#21262D";
        public static MudColor Dark900 { get; } = "#161B22";
        public static MudColor Dark950 { get; } = "#0D1117";

        public static MudColor Primary100 { get; } = "#C7E6C1";
        public static MudColor Primary200 { get; } = "#B5DEAD";
        public static MudColor Primary300 { get; } = "#9DD392";
        public static MudColor Primary400 { get; } = "#7CC46E";
        public static MudColor Primary500 { get; } = "#44A431";
        public static MudColor Primary600 { get; } = "#389725";
        public static MudColor Primary700 { get; } = "#2E8A1B";
        public static MudColor Primary800 { get; } = "#247D12";
        public static MudColor Primary900 { get; } = "#1C710B";

        public static MudColor Green100 { get; } = "#EEFAEB";
        public static MudColor Green200 { get; } = "#C7E8C0";
        public static MudColor Green300 { get; } = "#A0D695";
        public static MudColor Green400 { get; } = "#78C369";
        public static MudColor Green500 { get; } = "#51B13E";
        public static MudColor Green600 { get; } = "#419437";
        public static MudColor Green700 { get; } = "#307630";
        public static MudColor Green800 { get; } = "#205928";
        public static MudColor Green900 { get; } = "#0F3B21";

        public static MudColor Yellow100 { get; } = "#FFFAF0";
        public static MudColor Yellow200 { get; } = "#FFF2D9";
        public static MudColor Yellow300 { get; } = "#FFE3AC";
        public static MudColor Yellow400 { get; } = "#FFCF70";
        public static MudColor Yellow500 { get; } = "#FFC043";
        public static MudColor Yellow600 { get; } = "#BC8B2C";
        public static MudColor Yellow700 { get; } = "#997328";
        public static MudColor Yellow800 { get; } = "#674D1B";
        public static MudColor Yellow900 { get; } = "#543D10";

        public static MudColor Red100 { get; } = "#FFEFED";
        public static MudColor Red200 { get; } = "#FED7D2";
        public static MudColor Red300 { get; } = "#F1998E";
        public static MudColor Red400 { get; } = "#E85C4A";
        public static MudColor Red500 { get; } = "#E11900";
        public static MudColor Red600 { get; } = "#AB1300";
        public static MudColor Red700 { get; } = "#870F00";
        public static MudColor Red800 { get; } = "#5A0A00";
        public static MudColor Red900 { get; } = "#420105";

        public static MudColor Blue100 { get; } = "#B5E1E8";
        public static MudColor Blue200 { get; } = "#9DD7E1";
        public static MudColor Blue300 { get; } = "#7CCAD7";
        public static MudColor Blue400 { get; } = "#51B9C9";
        public static MudColor Blue500 { get; } = "#17A2B8";
        public static MudColor Blue600 { get; } = "#03899E";
        public static MudColor Blue700 { get; } = "#007D91";
        public static MudColor Blue800 { get; } = "#007284";
        public static MudColor Blue900 { get; } = "#006778";
    }

    // https://mudblazor.com/customization/default-theme
    // https://design.btcpayserver.org/styles/btcpayserver-variables.css
    public static readonly MudTheme Theme = new ()
    {
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Open Sans", "Helvetica Neue", "Arial", "sans-serif" },
                FontSize = "16px",
                LineHeight = 1.6,
                LetterSpacing = "0"
            },
            H1 = new H1
            {
                FontSize = "calc(1.34375rem + 1.125vw)",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            H2 = new H2
            {
                FontSize = "calc(1.3rem + 0.6vw)",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            H3 = new H3
            {
                FontSize = "calc(1.27813rem + 0.3375vw)",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            H4 = new H4
            {
                FontSize = "calc(1.25625rem + 0.075vw)",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            H5 = new H5
            {
                FontSize = "1.09375rem",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            H6 = new H6
            {
                FontSize = "0.875rem",
                FontWeight = 700,
                LineHeight = 1.2,
                LetterSpacing = "0"
            },
            Button = new Button
            {
                FontSize = "1rem",
                FontWeight = 600,
                LineHeight = 1.6,
                LetterSpacing = null,
                TextTransform = "none"
            }
        },
        Palette = new PaletteLight
        {
            Black  = Colors.Black,
            White  = Colors.White,
            Primary = Colors.Primary500,
            PrimaryDarken = Colors.Brand.Tertiary,
            PrimaryContrastText = Colors.White,
            Secondary = Colors.Primary500,
            SecondaryContrastText = Colors.Primary500,
            Tertiary = Colors.Light500,
            TertiaryContrastText = Colors.White,
            Info = Colors.Blue500,
            InfoContrastText = Colors.White,
            Success = Colors.Green500,
            SuccessContrastText = Colors.White,
            Warning = Colors.Yellow500,
            WarningContrastText = Colors.White,
            Error = Colors.Red500,
            ErrorContrastText = Colors.White,
            Dark = Colors.Light800,
            DarkContrastText = Colors.White,
            TextPrimary = Colors.Light900,
            TextSecondary = Colors.Light500,
            TextDisabled = Colors.Light600,
            ActionDefault = Colors.Light500,
            ActionDisabled = Colors.Light600,
            ActionDisabledBackground = Colors.Light200,
            Background = Colors.Light100,
            BackgroundGrey = Colors.White,
            Surface = Colors.Light100,
            DrawerBackground = Colors.White,
            DrawerText = Colors.Light900,
            DrawerIcon = Colors.Light900,
            AppbarBackground = Colors.White,
            AppbarText = Colors.Light900,
            LinesDefault = Colors.Light200,
            LinesInputs = Colors.Light300,
            TableLines = Colors.Light200,
            TableStriped = Colors.Light200,
            TableHover = Colors.White,
            Divider = Colors.Light300,
            DividerLight = Colors.Light200,
        },
        PaletteDark = new PaletteDark
        {
            Black  = Colors.Black,
            White  = Colors.White,
            Primary = Colors.Primary500,
            PrimaryDarken = Colors.Brand.Tertiary,
            PrimaryContrastText = Colors.White,
            Secondary = Colors.Dark900,
            SecondaryContrastText = Colors.Primary500,
            Tertiary = Colors.Dark500,
            TertiaryContrastText = Colors.Black,
            Info = Colors.Blue500,
            InfoContrastText = Colors.White,
            Success = Colors.Green500,
            SuccessContrastText = Colors.White,
            Warning = Colors.Yellow500,
            WarningContrastText = Colors.White,
            Error = Colors.Red500,
            ErrorContrastText = Colors.White,
            Dark = Colors.Dark200,
            DarkContrastText = Colors.Black,
            TextPrimary = Colors.White,
            TextSecondary = Colors.Dark500,
            TextDisabled = Colors.Dark400,
            ActionDefault = Colors.Dark500,
            ActionDisabled = Colors.Dark400,
            ActionDisabledBackground = Colors.Dark700,
            Background = Colors.Dark900,
            BackgroundGrey = Colors.Dark950,
            Surface = Colors.Dark900,
            DrawerBackground = Colors.Dark950,
            DrawerText = Colors.White,
            DrawerIcon = Colors.White,
            AppbarBackground = Colors.Dark950,
            AppbarText = Colors.White,
            LinesDefault = Colors.Dark800,
            LinesInputs = Colors.Dark700,
            TableLines = Colors.Dark800,
            TableStriped = Colors.Dark800,
            TableHover = Colors.Dark950,
            Divider = Colors.Dark700,
            DividerLight = Colors.Dark800,
        }
    };
}
