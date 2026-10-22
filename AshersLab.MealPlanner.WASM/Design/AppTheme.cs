using MudBlazor;

namespace AshersLab.MealPlanner.WASM.Design;

/// <summary>
/// The fridge-door pad in the kitchen bench's colours: white-enamel pad paper on a sage laminate ground, the pad's
/// printed ink in bottle green, the canister set (tomato, butter, sky, sage) for marker jobs and person magnets.
/// The same values are CSS custom properties in <c>wwwroot/css/app.css</c>; change one, change both.
/// </summary>
/// <remarks>
/// By day it is used in a lit kitchen and a supermarket aisle; at night (<see cref="Night"/>, the planner's choice on
/// Settings → Colours, never the phone's own setting) the same pad under the rangehood light. Tomato as text is the
/// darker <see cref="TomatoInk"/> - plain tomato on enamel is 3.85:1, below body-text contrast; plain tomato stays for
/// fills, the active tab's mark and strike-throughs.
/// </remarks>
public static class AppTheme
{
    public const string Bottle    = "#2F5D50";
    public const string Laminate  = "#DDE9DF";
    public const string Enamel    = "#FBFBF6";
    public const string Ink       = "#1D2B27";
    public const string InkSoft   = "#4E5F59";
    public const string Chrome    = "#B7C1BD";
    public const string Tomato    = "#D9533C";
    public const string TomatoInk = "#B8412C";
    public const string Butter    = "#F2C14E";
    public const string Sky       = "#7FB6CF";
    public const string Sage      = "#6FA27A";

    /// <summary>
    /// The pad at night - css/app.css's <c>:root[data-theme="dark"]</c>; change one, change both. The print lifts to a
    /// pale bottle green so it reads on the dark sheet, and parts from the band, which stays a deep bottle green; tomato
    /// ink lifts for text. Contrast on the sheet: ink 13.2:1, ink soft 7.2:1, print 7.9:1, tomato ink 7.3:1.
    /// </summary>
    public static class Night
    {
        public const string Print     = "#8CC4AE";
        public const string Band      = "#2A5246";
        public const string OnBand    = "#EEF1EA";
        public const string Laminate  = "#121A17";
        public const string Enamel    = "#1C2622";
        public const string Ink       = "#E9EEE8";
        public const string InkSoft   = "#A5B4AD";
        public const string Chrome    = "#64766F";
        public const string Rule      = "#2C3833";
        public const string TomatoInk = "#F49B85";
    }

    private static readonly string[] Body = ["Atkinson Hyperlegible", "system-ui", "-apple-system", "Segoe UI", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary              = Bottle,
            PrimaryContrastText  = Enamel,
            Secondary            = TomatoInk,
            SecondaryContrastText = Enamel,
            Tertiary             = "#2F6E8A",
            Info                 = "#2F6E8A",
            Success              = "#3F7A4E",
            // Attention is one marker colour app-wide: "how much?", "unpriced", running low all read in tomato.
            Warning              = TomatoInk,
            Error                = TomatoInk,
            Dark                 = Ink,
            Background           = Laminate,
            BackgroundGray       = "#D3E1D6",
            Surface              = Enamel,
            AppbarBackground     = Bottle,
            AppbarText           = Enamel,
            DrawerBackground     = Enamel,
            DrawerText           = Ink,
            DrawerIcon           = Bottle,
            TextPrimary          = Ink,
            TextSecondary        = InkSoft,
            TextDisabled         = "rgba(29,43,39,0.42)",
            ActionDefault        = InkSoft,
            LinesDefault         = "#D6DED9",
            LinesInputs          = Chrome,
            Divider              = "#D6DED9",
            TableLines           = "#D6DED9",
            Skeleton             = "rgba(47,93,80,0.10)",
        },
        PaletteDark = new PaletteDark
        {
            // A filled green button is the print's pale green with the sheet's dark ink on it: the band's deep green
            // would vanish into the dark sheet.
            Primary               = Night.Print,
            PrimaryContrastText   = Night.Enamel,
            Secondary             = Night.TomatoInk,
            SecondaryContrastText = Night.Enamel,
            Tertiary              = "#8DC0DA",
            Info                  = "#8DC0DA",
            Success               = "#8CC79A",
            Warning               = Night.TomatoInk,
            Error                 = Night.TomatoInk,
            Dark                  = Night.Laminate,
            Black                 = "#0B110F",
            White                 = Night.Ink,
            Background            = Night.Laminate,
            BackgroundGray        = "#18211E",
            Surface               = Night.Enamel,
            AppbarBackground      = Night.Band,
            AppbarText            = Night.OnBand,
            DrawerBackground      = Night.Enamel,
            DrawerText            = Night.Ink,
            DrawerIcon            = Night.Print,
            TextPrimary           = Night.Ink,
            TextSecondary         = Night.InkSoft,
            TextDisabled          = "rgba(233,238,232,0.40)",
            ActionDefault         = Night.InkSoft,
            ActionDisabled        = "rgba(233,238,232,0.30)",
            ActionDisabledBackground = "rgba(233,238,232,0.10)",
            LinesDefault          = Night.Rule,
            LinesInputs           = Night.Chrome,
            Divider               = Night.Rule,
            DividerLight          = "rgba(233,238,232,0.06)",
            TableLines            = Night.Rule,
            TableStriped          = "rgba(233,238,232,0.03)",
            TableHover            = "rgba(140,196,174,0.06)",
            Skeleton              = "rgba(140,196,174,0.10)",
            OverlayDark           = "rgba(0,0,0,0.6)",
            OverlayLight          = "rgba(18,26,23,0.5)",
        },
        Typography = new Typography
        {
            Default   = new DefaultTypography { FontFamily = Body, FontSize = "1rem", LineHeight = "1.45", LetterSpacing = "normal" },
            H1        = new H1Typography { FontFamily = Body, FontSize = "2.5rem", FontWeight = "700", LineHeight = "1.1", LetterSpacing = "-0.02em" },
            H2        = new H2Typography { FontFamily = Body, FontSize = "2rem", FontWeight = "700", LineHeight = "1.15", LetterSpacing = "-0.02em" },
            H3        = new H3Typography { FontFamily = Body, FontSize = "1.75rem", FontWeight = "700", LineHeight = "1.15", LetterSpacing = "-0.01em" },
            H4        = new H4Typography { FontFamily = Body, FontSize = "1.5rem", FontWeight = "700", LineHeight = "1.2", LetterSpacing = "-0.01em" },
            H5        = new H5Typography { FontFamily = Body, FontSize = "1.375rem", FontWeight = "700", LineHeight = "1.2", LetterSpacing = "-0.01em" },
            H6        = new H6Typography { FontFamily = Body, FontSize = "1.125rem", FontWeight = "700", LineHeight = "1.3", LetterSpacing = "normal" },
            Subtitle1 = new Subtitle1Typography { FontFamily = Body, FontSize = "1rem", FontWeight = "700", LineHeight = "1.4" },
            Subtitle2 = new Subtitle2Typography { FontFamily = Body, FontSize = "0.875rem", FontWeight = "700", LineHeight = "1.4" },
            Body1     = new Body1Typography { FontFamily = Body, FontSize = "1rem", FontWeight = "400", LineHeight = "1.45" },
            Body2     = new Body2Typography { FontFamily = Body, FontSize = "0.9375rem", FontWeight = "400", LineHeight = "1.45" },
            Button    = new ButtonTypography { FontFamily = Body, FontSize = "0.9375rem", FontWeight = "700", LineHeight = "1.2", TextTransform = "none", LetterSpacing = "normal" },
            Caption   = new CaptionTypography { FontFamily = Body, FontSize = "0.8125rem", FontWeight = "400", LineHeight = "1.4" },
            Overline  = new OverlineTypography { FontFamily = Body, FontSize = "0.8125rem", FontWeight = "700", LineHeight = "1.4", TextTransform = "none", LetterSpacing = "normal" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            AppbarHeight        = "56px",
        },
    };
}
