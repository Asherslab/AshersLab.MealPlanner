using MudBlazor;

namespace AshersLab.MealPlanner.WASM.Design;

/// <summary>
/// The fridge-door pad in the kitchen bench's colours: white-enamel pad paper on a sage laminate ground, the pad's
/// printed ink in bottle green, the canister set (tomato, butter, sky, sage) for marker jobs and person magnets.
/// The same values are CSS custom properties in <c>wwwroot/css/app.css</c>; change one, change both.
/// </summary>
/// <remarks>
/// Light only: the app is used in a lit kitchen and a supermarket aisle. Tomato as text is the darker
/// <see cref="TomatoInk"/> - plain tomato on enamel is 3.85:1, below body-text contrast; plain tomato stays for
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
