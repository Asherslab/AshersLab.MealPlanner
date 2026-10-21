using System.Globalization;
using System.Text.RegularExpressions;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Shared.Domain.Products;

/// <summary>
/// A product's pack quantity as the planner types and reads it: "2kg", "8x250g", "1.25 L", "18pk". Always held in the
/// ingredient's base unit - grams, millilitres or each - so "2kg" is 2000.
/// </summary>
/// <remarks>
/// Only the units of the ingredient's own kind are accepted: "500ml" for an ingredient counted in grams is refused,
/// never converted, because there is no conversion between grams and millilitres (<see cref="BaseUnit"/>).
/// </remarks>
public static partial class PackSize
{
    /// <summary>The largest pack: a tonne, a thousand litres, a million things. Anything bigger is a typo.</summary>
    public const decimal Max = 1_000_000m;

    /// <summary>Thousandths of a gram, millilitre or each - more is a typo, not precision.</summary>
    public const int MaxDecimals = 3;

    // "8 x 250 g", "8×250g", "1.4KG", "2000", "18pk", "1,5 kg" is not accepted (the comma is a thousands separator to
    // a phone in en-AU, and a typo here would be silent).
    [GeneratedRegex(@"^\s*(?:(?<count>\d+)\s*[x×*]\s*)?(?<amount>\d+(?:\.\d+)?|\.\d+)\s*(?<unit>[a-zA-Z]*)\s*$")]
    private static partial Regex Shape();

    /// <summary>
    /// Reads what the planner typed into a quantity in <paramref name="unit"/>; null with a reason in
    /// <paramref name="problem"/> when it cannot.
    /// </summary>
    public static decimal? Parse(string? text, BaseUnit unit, out string? problem)
    {
        problem = null;
        Match match = Shape().Match(text ?? "");
        if (!match.Success)
        {
            problem = string.IsNullOrWhiteSpace(text) ? "Enter how much is in a pack." : $"Type a quantity like {Example(unit)}.";
            return null;
        }

        decimal? factor = Factor(match.Groups["unit"].Value.ToLowerInvariant(), unit);
        if (factor is null)
        {
            problem = $"It is counted in {unit.Symbol()} - type a quantity like {Example(unit)}.";
            return null;
        }

        decimal amount = decimal.Parse(match.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        decimal count  = match.Groups["count"].Success ? decimal.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : 1;
        decimal total  = count * amount * factor.Value;

        problem = Problem(total);
        return problem is null ? total : null;
    }

    /// <summary>Null when <paramref name="quantity"/> (in the base unit) is a pack size; otherwise why not.</summary>
    public static string? Problem(decimal quantity) => quantity switch
    {
        <= 0                                => "A pack has to hold something.",
        > Max                               => "That is more than any pack.",
        _ when decimal.Round(quantity, MaxDecimals) != quantity
                                            => $"Keep it to {MaxDecimals} decimal places.",
        _                                   => null,
    };

    /// <summary>What the planner reads: "2 kg", "500 g", "1.25 L", "330 ml", "12 each".</summary>
    public static string Format(decimal quantity, BaseUnit unit) => unit switch
    {
        BaseUnit.Gram       when quantity >= 1000 => $"{Number(quantity / 1000)} kg",
        BaseUnit.Millilitre when quantity >= 1000 => $"{Number(quantity / 1000)} L",
        _                                         => $"{Number(quantity)} {unit.Symbol()}",
    };

    /// <summary>What goes back into the box for editing: "2kg" reads back as 2000 g.</summary>
    public static string Editable(decimal quantity, BaseUnit unit) => Format(quantity, unit).Replace(" ", "");

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Example(BaseUnit unit) => unit switch
    {
        BaseUnit.Gram       => "500g, 2kg or 8x250g",
        BaseUnit.Millilitre => "330ml, 2L or 6x375ml",
        _                   => "12 or 6x2",
    };

    /// <summary>How many base units one of the typed unit is; null when it is not this ingredient's kind.</summary>
    private static decimal? Factor(string typed, BaseUnit unit) => (unit, typed) switch
    {
        (BaseUnit.Gram, "" or "g" or "gm" or "gms" or "gram" or "grams")                         => 1m,
        (BaseUnit.Gram, "kg" or "kgs" or "kilo" or "kilos")                                       => 1000m,
        (BaseUnit.Millilitre, "" or "ml" or "mls")                                                => 1m,
        (BaseUnit.Millilitre, "l" or "lt" or "litre" or "litres" or "liter" or "liters")          => 1000m,
        (BaseUnit.Each, "" or "each" or "ea" or "pk" or "pack" or "ct" or "x")                    => 1m,
        _                                                                                         => null,
    };
}
