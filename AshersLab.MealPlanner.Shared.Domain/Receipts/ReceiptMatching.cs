using System.Text.RegularExpressions;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Products;

namespace AshersLab.MealPlanner.Shared.Domain.Receipts;

/// <summary>A product a receipt line could be: at the receipt's store, with its code and the descriptions it has been matched to.</summary>
public sealed record MatchProduct(Guid Id, string Name, string? StoreCode, Guid? IngredientId);

/// <summary>An ingredient a new product could be made under.</summary>
public sealed record MatchIngredient(Guid Id, string Name);

public enum MatchedBy
{
    /// <summary>Nothing matched: the planner picks, or it is imported unmatched.</summary>
    None,

    /// <summary>The store's own code for the product: certain.</summary>
    StoreCode,

    /// <summary>A description this product was matched to before: certain, and what makes the second receipt automatic.</summary>
    Alias,
}

/// <summary>A guess the planner confirms - never applied by itself.</summary>
/// <param name="Score">0 to 1: how much of the name the description shares.</param>
public sealed record MatchSuggestion(Guid? ProductId, Guid? IngredientId, string Name, double Score);

/// <summary>What a line matched, and what it might be.</summary>
public sealed record LineMatch(Guid? ProductId, MatchedBy By, IReadOnlyList<MatchSuggestion> Suggestions);

/// <summary>
/// Which product a receipt line is, in the order the receipt-formats research settled: <b>the store's code</b>, then
/// <b>an alias</b> (a description matched before), then <b>suggestions by name</b> that the planner confirms. Only the
/// first two match by themselves; a suggestion is a guess, and a wrong automatic match would price and stock the wrong
/// thing silently. Confirming one teaches its alias, so the next receipt matches at step two. Pure.
/// </summary>
public static partial class ReceiptMatching
{
    /// <summary>A suggestion below this shares too little of the name to be worth a tap.</summary>
    public const double MinScore = 0.5;

    public const int MaxSuggestions = 4;

    // Words a receipt adds that say nothing about what the thing is: the store's own brand, its "price dropped" marker,
    // pack words. Pack sizes ("700g", "1l", "4") are dropped by shape.
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "woolworths", "essentials", "macro", "pdm", "pack", "pk", "each", "ea", "punnet", "value", "the", "and", "with", "of", "x",
    };

    [GeneratedRegex(@"[a-z]+|\d+(?:\.\d+)?[a-z]*")]
    private static partial Regex Word();

    [GeneratedRegex(@"^\d")]
    private static partial Regex Numeric();

    /// <param name="aliases">The store's aliases: <see cref="ProductAliasRules.Key"/> of a description, and its product.</param>
    public static LineMatch Match(ParsedLine line, IReadOnlyList<MatchProduct> products, IReadOnlyDictionary<string, Guid> aliases,
                                  IReadOnlyList<MatchIngredient> ingredients)
    {
        Dictionary<Guid, MatchProduct> byId = products.ToDictionary(p => p.Id);
        if (line.StoreCode is { } code && products.FirstOrDefault(p => p.StoreCode is { } c && string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) is { } coded)
            return new LineMatch(coded.Id, MatchedBy.StoreCode, []);
        if (aliases.TryGetValue(ProductAliasRules.Key(line.Description), out Guid aliased) && byId.ContainsKey(aliased))
            return new LineMatch(aliased, MatchedBy.Alias, []);
        return new LineMatch(null, MatchedBy.None, Suggest(line.Description, products, ingredients));
    }

    /// <summary>
    /// Products whose name the description shares most of, then ingredients whose every word is in it ("black beans" in
    /// "Woolworths black beans pdm 420g") to make a product under. Best first; ties by name, so every device lists the same.
    /// </summary>
    public static IReadOnlyList<MatchSuggestion> Suggest(string description, IReadOnlyList<MatchProduct> products, IReadOnlyList<MatchIngredient> ingredients)
    {
        HashSet<string> words = Words(description);
        if (words.Count == 0)
            return [];
        IEnumerable<MatchSuggestion> fromProducts = products
            .Select(p => new MatchSuggestion(p.Id, p.IngredientId, p.Name, Dice(words, Words(p.Name))));
        IEnumerable<MatchSuggestion> fromIngredients = ingredients
            .Select(i => new MatchSuggestion(null, i.Id, i.Name, Covered(Words(i.Name), words) * 0.9));
        return [.. fromProducts.Concat(fromIngredients)
            .Where(s => s.Score >= MinScore)
            .OrderByDescending(s => s.Score).ThenBy(s => s.ProductId is null).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSuggestions)];
    }

    /// <summary>The words that say what a thing is: lower case, plurals folded, pack sizes and noise dropped.</summary>
    public static HashSet<string> Words(string text) =>
        [.. Word().Matches(text.ToLowerInvariant()).Select(m => m.Value)
            .Where(w => !Numeric().IsMatch(w) && !Noise.Contains(w) && w.Length > 1)
            .Select(Singular)];

    private static string Singular(string word) =>
        word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4 ? word[..^3] + "y"
        : word.EndsWith("oes", StringComparison.Ordinal) && word.Length > 4 ? word[..^2]
        : word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && word.Length > 3 ? word[..^1]
        : word;

    /// <summary>How alike two sets of words are: twice the shared words over both counts.</summary>
    public static double Dice(IReadOnlySet<string> a, IReadOnlySet<string> b) =>
        a.Count + b.Count == 0 ? 0 : 2.0 * a.Count(b.Contains) / (a.Count + b.Count);

    /// <summary>How much of <paramref name="name"/> is in <paramref name="description"/>.</summary>
    public static double Covered(IReadOnlySet<string> name, IReadOnlySet<string> description) =>
        name.Count == 0 ? 0 : (double)name.Count(description.Contains) / name.Count;

    [GeneratedRegex(@"(?<count>\d+)\s*x\s*(?<size>\d+(?:\.\d+)?)\s*(?<unit>kg|g|ml|l)\b|(?<size>\d+(?:\.\d+)?)\s*(?<unit>kg|g|ml|l|pack|pk|ct)\b|\b(?<each>each)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PackShape();

    /// <summary>
    /// The pack size a description prints, read in the ingredient's unit: "...bread 700g" is 700 g, "...4 pack" 4 each,
    /// "...tomato each" 1, "...milk uht 1l" 1000 ml. The last size printed wins ("459g - 749g" is the heaviest tray). Null
    /// when it prints none, or one in another kind of unit - never converted, as everywhere.
    /// </summary>
    public static decimal? PackGuess(string description, BaseUnit unit)
    {
        Match? last = PackShape().Matches(description).LastOrDefault();
        if (last is null)
            return null;
        if (last.Groups["each"].Success)
            return unit == BaseUnit.Each ? 1 : null;
        string text = (last.Groups["count"].Success ? last.Groups["count"].Value + "x" : "") + last.Groups["size"].Value + last.Groups["unit"].Value;
        return PackSize.Parse(text, unit, out _);
    }
}
