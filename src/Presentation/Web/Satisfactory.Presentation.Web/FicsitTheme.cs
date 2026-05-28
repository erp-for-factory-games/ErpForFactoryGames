using MudBlazor;

namespace Satisfactory.Presentation.Web;

/// <summary>
/// MudBlazor theme carrying the FICSIT palette. The colour tokens here mirror
/// the <c>--fx-*</c> CSS variables in <c>wwwroot/app.css</c> — see that file for
/// the canonical source of the brand colours. Bespoke decorations (hazard tape,
/// status LEDs, corner brackets, icon masks, schematic-grid background) stay
/// in app.css; only component-facing colours live here.
/// </summary>
public static class FicsitTheme
{
    public static readonly MudTheme Instance = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#FA9549",          // --fx-orange
            PrimaryDarken = "#C77437",    // --fx-orange-dim
            PrimaryLighten = "#FFB070",   // --fx-orange-bright
            Secondary = "#FFC53D",        // --fx-yellow
            SecondaryDarken = "#C9941F",  // --fx-yellow-dim
            Tertiary = "#5FB0C9",         // --fx-info
            Info = "#5FB0C9",             // --fx-info
            Success = "#5FC97C",          // --fx-success
            Warning = "#F2A93B",          // --fx-caution
            Error = "#E5604A",            // --fx-danger

            Black = "#000000",
            White = "#FFFFFF",

            Background = "#16161A",       // --fx-bg
            Surface = "#1F1F25",          // --fx-bg-elevated
            AppbarBackground = "#1F1F25",
            AppbarText = "#E8E8EB",       // --fx-text
            DrawerBackground = "#1B1B20",
            DrawerText = "#E8E8EB",
            DrawerIcon = "#9A9AA0",       // --fx-text-muted

            TextPrimary = "#E8E8EB",      // --fx-text
            TextSecondary = "#9A9AA0",    // --fx-text-muted
            TextDisabled = "#6B6B72",     // --fx-text-dim
            ActionDefault = "#9A9AA0",
            ActionDisabled = "#6B6B72",
            ActionDisabledBackground = "#2A2A30",

            LinesDefault = "#3A3A42",     // --fx-border
            LinesInputs = "#4A4A52",      // --fx-border-strong
            Divider = "#2F2F35",          // --fx-border-soft
            DividerLight = "#252529",

            HoverOpacity = 0.08,
        },
        PaletteLight = new PaletteLight
        {
            Primary = "#C77437",          // --fx-orange-dim (light mode uses dimmer orange for contrast)
            PrimaryDarken = "#A55F2A",
            PrimaryLighten = "#FA9549",
            Secondary = "#C9941F",        // --fx-yellow-dim
            SecondaryDarken = "#A07815",
            Tertiary = "#3A8FA8",
            Info = "#3A8FA8",
            Success = "#3E9F5C",
            Warning = "#D08818",
            Error = "#C04830",

            Black = "#000000",
            White = "#FFFFFF",

            Background = "#F0EBE0",       // --fx-bg (light parchment)
            Surface = "#FFFFFF",          // --fx-bg-elevated
            AppbarBackground = "#FAF6EC", // --fx-bg-panel
            AppbarText = "#1B1B1F",       // --fx-text
            DrawerBackground = "#FAF6EC",
            DrawerText = "#1B1B1F",
            DrawerIcon = "#6A6A70",

            TextPrimary = "#1B1B1F",
            TextSecondary = "#6A6A70",
            TextDisabled = "#98948A",
            ActionDefault = "#6A6A70",
            ActionDisabled = "#98948A",
            ActionDisabledBackground = "#E0D8C4",

            LinesDefault = "#D4CBB6",     // --fx-border
            LinesInputs = "#B0A688",      // --fx-border-strong
            Divider = "#E0D8C4",          // --fx-border-soft
            DividerLight = "#ECE5D2",

            HoverOpacity = 0.06,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Segoe UI Variable Display", "Segoe UI Variable", "Segoe UI", "Inter", "system-ui", "sans-serif"],
            },
            H1 = new H1Typography
            {
                FontFamily = ["Bahnschrift Condensed", "Bahnschrift", "Inter Tight", "Segoe UI Variable Display", "sans-serif"],
                FontWeight = "700",
                TextTransform = "uppercase",
                LetterSpacing = "0.06em",
            },
            H2 = new H2Typography
            {
                FontFamily = ["Bahnschrift Condensed", "Bahnschrift", "Inter Tight", "sans-serif"],
                FontWeight = "600",
                TextTransform = "uppercase",
                LetterSpacing = "0.04em",
            },
        },
    };
}
