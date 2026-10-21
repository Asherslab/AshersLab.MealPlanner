using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

/// <summary>
/// What an imported ingredient is probably counted in. The sheet never says, so every answer here is a guess and is
/// saved marked as one (<c>IngredientRules.UnitGuessedField</c>) until the planner confirms it.
/// </summary>
/// <remarks>
/// By the words of its name first, the most specific word winning - "lime juice" is millilitres though "lime" is each,
/// "tinned tomato" grams though "tomato" is each - then by its shopping category, then grams.
/// </remarks>
public static class UnitGuess
{
    private static readonly (string Words, BaseUnit Unit)[] ByWords =
    [
        // Whole things.
        ("bagel", BaseUnit.Each), ("egg", BaseUnit.Each), ("wrap", BaseUnit.Each), ("tortilla", BaseUnit.Each),
        ("avocado", BaseUnit.Each), ("banana", BaseUnit.Each), ("apple", BaseUnit.Each), ("bread", BaseUnit.Each),
        ("roll", BaseUnit.Each), ("bar", BaseUnit.Each), ("pouch", BaseUnit.Each), ("stick", BaseUnit.Each),
        ("mi goreng", BaseUnit.Each), ("cucumber", BaseUnit.Each), ("lettuce", BaseUnit.Each), ("carrot", BaseUnit.Each),
        ("onion", BaseUnit.Each), ("potato", BaseUnit.Each), ("tomato", BaseUnit.Each), ("celery", BaseUnit.Each),
        ("lime", BaseUnit.Each), ("lemon", BaseUnit.Each), ("pizza", BaseUnit.Each), ("chook", BaseUnit.Each),
        ("roast chicken", BaseUnit.Each), ("capsicum", BaseUnit.Each), ("zucchini", BaseUnit.Each),
        ("garlic bread", BaseUnit.Each), ("frozen pizza", BaseUnit.Each), ("yoghurt pouch", BaseUnit.Each),
        // Poured.
        ("sauce", BaseUnit.Millilitre), ("milk", BaseUnit.Millilitre), ("oil", BaseUnit.Millilitre),
        ("juice", BaseUnit.Millilitre), ("vinegar", BaseUnit.Millilitre), ("mayonnaise", BaseUnit.Millilitre),
        ("mayo", BaseUnit.Millilitre), ("cream", BaseUnit.Millilitre), ("bolognese", BaseUnit.Millilitre),
        ("stock", BaseUnit.Millilitre),
        // Weighed.
        ("cheese", BaseUnit.Gram), ("rice", BaseUnit.Gram), ("pasta", BaseUnit.Gram), ("spaghetti", BaseUnit.Gram),
        ("risoni", BaseUnit.Gram), ("ravioli", BaseUnit.Gram), ("noodle", BaseUnit.Gram), ("beef", BaseUnit.Gram),
        ("mince", BaseUnit.Gram), ("chicken", BaseUnit.Gram), ("bacon", BaseUnit.Gram), ("ham", BaseUnit.Gram),
        ("tuna", BaseUnit.Gram), ("fish", BaseUnit.Gram), ("schnitzel", BaseUnit.Gram), ("chip", BaseUnit.Gram),
        ("bean", BaseUnit.Gram), ("pea", BaseUnit.Gram), ("corn", BaseUnit.Gram), ("seasoning", BaseUnit.Gram),
        ("powder", BaseUnit.Gram), ("flour", BaseUnit.Gram), ("sugar", BaseUnit.Gram), ("berry", BaseUnit.Gram),
        ("blueberry", BaseUnit.Gram), ("mango", BaseUnit.Gram), ("garlic", BaseUnit.Gram), ("oat", BaseUnit.Gram),
        ("yoghurt", BaseUnit.Gram), ("butter", BaseUnit.Gram),
        ("diced", BaseUnit.Gram), ("tinned", BaseUnit.Gram), ("frozen", BaseUnit.Gram), ("cream of", BaseUnit.Gram),
        ("sour cream", BaseUnit.Gram), ("tinned tomato", BaseUnit.Gram), ("diced onion", BaseUnit.Gram),
    ];

    /// <summary>Shopping categories as the seeds name them; anything else falls to grams.</summary>
    private static readonly Dictionary<string, BaseUnit> ByCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["meat"] = BaseUnit.Gram, ["dairy"] = BaseUnit.Gram, ["fruit"] = BaseUnit.Each, ["vegetable"] = BaseUnit.Each,
        ["carb side"] = BaseUnit.Gram, ["pantry"] = BaseUnit.Gram, ["fridge"] = BaseUnit.Millilitre, ["freezer"] = BaseUnit.Gram,
    };

    public static BaseUnit For(string name, string? category)
    {
        string key = " " + ImportNames.Key(name) + " ";
        (string Words, BaseUnit Unit)? best = null;
        foreach ((string words, BaseUnit unit) in ByWords)
        {
            if (!key.Contains(" " + ImportNames.Key(words) + " ", StringComparison.Ordinal))
                continue;
            if (best is null || ImportNames.Key(words).Length > ImportNames.Key(best.Value.Words).Length)
                best = (words, unit);
        }
        if (best is { } found)
            return found.Unit;

        return category is not null && ByCategory.TryGetValue(ImportNames.Key(category), out BaseUnit byCategory)
            ? byCategory
            : BaseUnit.Gram;
    }
}
