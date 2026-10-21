using System.Globalization;
using System.Text.RegularExpressions;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>
/// A price written in the sheet: "14.50/24", "$12.50/kg", "4.50 Costco", or a number in a price column.
/// <paramref name="Pack"/> is what the price is for as typed ("24", "1kg", "6x375ml"), null when it does not say;
/// <paramref name="Store"/> the store word found beside it, as one of the words the caller knows.
/// </summary>
public sealed record SheetPrice(long Cents, string? Pack, string? Store);

/// <summary>An ingredient a meal idea names, with the alternatives the sheet gave for it ("grated cheese/lime juice").</summary>
public sealed record IngredientRef(string Name, IReadOnlyList<string> Alternatives);

/// <summary>
/// One meal-ideas cell: <c>4x Spaghetti bolognese (spaghetti, beef, bolognese)</c>. <paramref name="Serves"/> is the
/// "4x", null when there is none; <paramref name="ServesUnsure"/> for the sheet's "8?x".
/// <paramref name="Ingredients"/> is null when the cell has no parentheses at all - a simple meal or a side - and empty
/// for "()". <paramref name="Unsure"/> when the cell carries a "?" anywhere: the planner was not sure of something.
/// </summary>
public sealed record MealIdea(
    string Name,
    int? Serves,
    bool ServesUnsure,
    IReadOnlyList<IngredientRef>? Ingredients,
    bool Unclosed,
    string? Trailing,
    bool Unsure,
    string Original);

/// <summary>
/// One plan cell: "Leftovers/mi goreng/pizza" is three choices; "Bagels, scrambled eggs + toast" is one choice of three
/// items. Read here, applied by the plan-template slice.
/// </summary>
public sealed record PlanCell(string Original, IReadOnlyList<IReadOnlyList<string>> Choices);

/// <summary>The parsers for what one cell says. Pure, tested in <c>Tests/Import/SheetTextTests.cs</c>.</summary>
public static partial class SheetText
{
    /// <summary>Store names the planner writes for a store whose name in the app is different.</summary>
    public static readonly IReadOnlyDictionary<string, string> StoreAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Woolies"]  = "Woolworths",
            ["Woollies"] = "Woolworths",
            ["Woolys"]   = "Woolworths",
        };

    // "$12.50", "12.50", "14.50/24", "12/kg", "4.50 per kg", "3 for 2L", "1.20 each". The store word is taken out first.
    [GeneratedRegex(@"^(?<dollar>\$)?\s*(?<amount>\d+(?:\.\d+)?|\.\d+)\s*(?:(?:/|\bper\b|\bfor\b|\ba\b)\s*(?<pack>\S.*?)|(?<each>\beach\b|\bea\b))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PriceShape();

    // What a pack says: "24", "kg", "2kg", "6x375ml", "1.5 L".
    [GeneratedRegex(@"^(?:(?<count>\d+)\s*[x×*]\s*)?(?<amount>\d+(?:\.\d+)?)?\s*(?<unit>[a-zA-Z]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PackShape();

    // A pack size at the end of a name, with a unit: "Milk 2L", "Mince 8x250g".
    [GeneratedRegex(@"^(?<name>.*\S)\s+(?<pack>(?:\d+\s*[x×*]\s*)?\d+(?:\.\d+)?\s*(?:g|kg|ml|l|pk|ct))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingPack();

    [GeneratedRegex(@"^\s*(?:(?<serves>\d+)\s*(?<q1>\?)?\s*[x×]\s*(?<q2>\?)?\s+)?(?<name>[^(]*?)\s*(?:\((?<inside>[^)]*)(?<close>\))?(?<rest>.*))?$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MealShape();

    /// <summary>
    /// A price on its own: the whole text is one. <paramref name="alone"/> when nothing else can be in the cell - a
    /// number cell, or a cell in a price column - so a bare "12" is a price. In a name cell a price must look like
    /// one: a "$", two decimal places, or what it is for ("12/kg"); "Eggs 12" is not a price.
    /// </summary>
    public static SheetPrice? Price(string text, IReadOnlyCollection<string> storeWords, bool alone)
    {
        string rest = TakeStore(Names.Normalise(text), storeWords, out string? store).Trim(' ', '-', ':', '@', ',', '(', ')');
        Match match = PriceShape().Match(rest);
        if (!match.Success)
            return null;

        string amountText = match.Groups["amount"].Value;
        int    decimals   = amountText.Contains('.') ? amountText.Length - amountText.IndexOf('.') - 1 : 0;
        bool   looksLike  = match.Groups["dollar"].Success || decimals == 2 || match.Groups["pack"].Success || match.Groups["each"].Success;
        if (!alone && !looksLike)
            return null;

        if (!decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal dollars))
            return null;
        decimal cents = dollars * 100;
        // A number cell can hold 4.4999999999999998 for 4.50 - a float the sheet stored, not a price in fractions of a
        // cent. Typed text with three decimals is refused, never rounded: it is a typo.
        if (decimal.Round(cents) != cents)
        {
            if (!alone || Math.Abs(decimal.Round(cents) - cents) > 0.0001m)
                return null;
            cents = decimal.Round(cents);
        }
        if (cents > Money.MaxCents || Money.CentsProblem((long)cents) is not null)
            return null;

        string? pack = match.Groups["each"].Success ? "1" : match.Groups["pack"].Success ? Pack(match.Groups["pack"].Value) : null;
        if (match.Groups["pack"].Success && pack is null)
            return null;
        return new SheetPrice((long)cents, pack, store);
    }

    /// <summary>"kg" is "1kg", "24" stays; null when it is not a pack at all.</summary>
    private static string? Pack(string text)
    {
        string pack = text.Trim();
        Match  m    = PackShape().Match(pack);
        if (!m.Success || (!m.Groups["amount"].Success && m.Groups["unit"].Value.Length == 0))
            return null;
        if (m.Groups["count"].Success && !m.Groups["amount"].Success)
            return null;
        return m.Groups["amount"].Success ? pack.Replace(" ", "") : "1" + m.Groups["unit"].Value;
    }

    /// <summary>
    /// A shopping-category cell: the ingredient's name, and a price if one is written after it ("Toilet rolls 14.50/24",
    /// "Beef $12.50/kg Costco"). <paramref name="usualStore"/> is a store named with no price, "Beef (Costco)".
    /// </summary>
    public static (string Name, SheetPrice? Price, string? UsualStore) CostItem(string text, IReadOnlyCollection<string> storeWords)
    {
        string cell = Names.Normalise(text);
        string[] words = cell.Split(' ');
        for (int i = 1; i < words.Length; i++)
        {
            string name = string.Join(' ', words[..i]).Trim(' ', '-', ':', '@', ',', '(');
            if (!name.Any(char.IsLetter))
                continue;
            if (Price(string.Join(' ', words[i..]), storeWords, alone: false) is not { } price)
                continue;

            // "Milk 2L 3.10": the pack is in the name.
            if (price.Pack is null && TrailingPack().Match(name) is { Success: true } trailing)
                return (ImportNames.Tidy(trailing.Groups["name"].Value), price with { Pack = trailing.Groups["pack"].Value.Replace(" ", "") }, null);
            return (ImportNames.Tidy(name), price, null);
        }

        string bare = TakeStore(cell, storeWords, out string? store, onlyAtEnd: true);
        return (ImportNames.Tidy(bare.TrimEnd(' ', '-', '@', '(', ')')), null, store);
    }

    /// <summary>
    /// Takes a store word out of <paramref name="text"/> - "(Costco)", "@ Woolies", "- Costco", or anywhere as a whole
    /// word unless <paramref name="onlyAtEnd"/>. The longest word known wins.
    /// </summary>
    private static string TakeStore(string text, IReadOnlyCollection<string> storeWords, out string? store, bool onlyAtEnd = false)
    {
        foreach (string word in storeWords.OrderByDescending(w => w.Length))
        {
            string pattern = onlyAtEnd
                ? $@"\s*(?:\(\s*{Regex.Escape(word)}\s*\)|[-@]\s*{Regex.Escape(word)}|\bat\s+{Regex.Escape(word)})\s*$"
                : $@"\s*(?:\(\s*{Regex.Escape(word)}\s*\)|(?:[-@]|\bat\b)?\s*\b{Regex.Escape(word)}\b)";
            Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                store = word;
                return Names.Normalise(text.Remove(match.Index, match.Length).Insert(match.Index, " "));
            }
        }
        store = null;
        return text;
    }

    /// <summary>
    /// A meal-ideas cell. The "Nx" is optional and is sometimes "8?x"; the parentheses are sometimes not closed, and
    /// then everything after the "(" is the ingredients. Ingredients are split on commas; a '/' or " or " inside one is
    /// alternatives - the first is the ingredient, the rest are noted.
    /// </summary>
    public static MealIdea MealIdea(string text)
    {
        string cell  = Names.Normalise(text);
        Match  match = MealShape().Match(cell);

        int? serves = match.Groups["serves"].Success
                      && int.TryParse(match.Groups["serves"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;
        bool unsureServes = match.Groups["q1"].Success || match.Groups["q2"].Success;

        List<IngredientRef>? ingredients = null;
        if (match.Groups["inside"].Success)
        {
            ingredients = [];
            foreach (string part in match.Groups["inside"].Value.Split(','))
            {
                string[] alternatives = [.. AlternativeSplit().Split(part).Select(Names.Normalise).Where(a => a.Trim('?').Length > 0)];
                if (alternatives.Length > 0)
                    ingredients.Add(new IngredientRef(alternatives[0], alternatives[1..]));
            }
        }

        string rest = Names.Normalise(match.Groups["rest"].Value).Trim(' ', ',', ')');
        return new MealIdea(
            ImportNames.Tidy(match.Groups["name"].Value),
            serves,
            unsureServes,
            ingredients,
            Unclosed: match.Groups["inside"].Success && !match.Groups["close"].Success,
            Trailing: rest.Length > 0 ? rest : null,
            Unsure: cell.Contains('?'),
            Original: cell);
    }

    [GeneratedRegex(@"\s*/\s*|\s+or\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlternativeSplit();

    [GeneratedRegex(@"\s*,\s*|\s*\+\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ItemSplit();

    /// <summary>
    /// A plan cell: '/' separates choices, ',' and '+' the items of one choice. "and" does not split - "Fish and
    /// chips" and "peas and corn" are one thing each.
    /// </summary>
    public static PlanCell PlanCell(string text)
    {
        string cell = Names.Normalise(text);
        List<IReadOnlyList<string>> choices = [];
        foreach (string choice in cell.Split('/'))
        {
            string[] items = [.. ItemSplit().Split(choice).Select(Names.Normalise).Where(i => i.Length > 0)];
            if (items.Length > 0)
                choices.Add(items);
        }
        return new PlanCell(cell, choices);
    }

    /// <summary>A plan row's slot: the leading word of "Lunch (sandwich for ...)".</summary>
    public static string SlotName(string label)
    {
        string normal = Names.Normalise(label);
        int    end    = 0;
        while (end < normal.Length && char.IsLetter(normal[end]))
            end++;
        return end == 0 ? normal : normal[..end];
    }
}
