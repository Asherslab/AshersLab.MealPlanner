using System.Globalization;
using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>
/// A quantity of an ingredient as the planner types and reads it - "500g", "1.5kg", "8x250g", "2" - always held in the
/// ingredient's base unit, so "1.5kg" is 1500. A pack's size (<c>PackSize</c>) and a recipe's amount
/// (<c>MealIngredientRules</c>) are both read this way, each with its own limits and messages.
/// </summary>
/// <remarks>
/// Only the units of the ingredient's own kind are accepted: "500ml" for an ingredient counted in grams is refused,
/// never converted, because there is no conversion between grams and millilitres (<see cref="BaseUnit"/>).
/// </remarks>
public static partial class Quantity
{
    /// <summary>A tonne, a thousand litres, a million things. Anything bigger is a typo.</summary>
    public const decimal Max = 1_000_000m;

    /// <summary>Thousandths of a gram, millilitre or each - more is a typo, not precision.</summary>
    public const int MaxDecimals = 3;

    // "8 x 250 g", "8×250g", "1.4KG", "2000", "18pk". "1,5 kg" is not accepted (the comma is a thousands separator to
    // a phone in en-AU, and a typo here would be silent).
    [GeneratedRegex(@"^\s*(?:(?<count>\d+)\s*[x×*]\s*)?(?<amount>\d+(?:\.\d+)?|\.\d+)\s*(?<unit>[a-zA-Z]*)\s*$")]
    private static partial Regex Shape();

    /// <summary>
    /// What was typed, in <paramref name="unit"/>, before any limit is checked. Null with a reason in
    /// <paramref name="problem"/> when it cannot be read; null with no problem when nothing was typed.
    /// </summary>
    public static decimal? Read(string? text, BaseUnit unit, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = Shape().Match(text);
        if (!match.Success)
        {
            problem = $"Type a quantity like {Example(unit)}.";
            return null;
        }

        decimal? factor = Factor(match.Groups["unit"].Value.ToLowerInvariant(), unit);
        if (factor is null)
        {
            problem = $"It is counted in {unit.Symbol()} - type a quantity like {Example(unit)}.";
            return null;
        }

        // The shape allows any number of digits, so a pasted run of them is more than a decimal holds: refused, never
        // thrown - this runs while the sheet renders its helper text.
        decimal count = 1;
        if (!decimal.TryParse(match.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)
            || (match.Groups["count"].Success && !decimal.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count)))
        {
            problem = TooBig;
            return null;
        }

        try
        {
            return count * amount * factor.Value;
        }
        catch (OverflowException)
        {
            problem = TooBig;
            return null;
        }
    }

    private const string TooBig = "That number is too big.";

    /// <summary>
    /// Null when <paramref name="quantity"/> is within the limits every quantity shares; otherwise why not, with
    /// <paramref name="tooSmall"/> or <paramref name="tooBig"/> saying it in the caller's words.
    /// </summary>
    public static string? Problem(decimal quantity, string tooSmall, string tooBig) => quantity switch
    {
        <= 0                                => tooSmall,
        > Max                               => tooBig,
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

    /// <summary>
    /// What goes back into the box for editing: "2kg" reads back as 2000 g. Every digit is kept - 1234.5 g is "1.2345kg",
    /// not <see cref="Format"/>'s "1.235 kg" - because Save reads the box again, and a rounded box would change the
    /// quantity (and send an op) without the planner touching it.
    /// </summary>
    public static string Editable(decimal quantity, BaseUnit unit) => unit switch
    {
        BaseUnit.Gram       when quantity >= 1000 => $"{Exact(quantity / 1000)}kg",
        BaseUnit.Millilitre when quantity >= 1000 => $"{Exact(quantity / 1000)}L",
        _                                         => $"{Exact(quantity)}{unit.Symbol()}",
    };

    /// <summary>"500g, 2kg or 8x250g" - what to type for this unit.</summary>
    public static string Example(BaseUnit unit) => unit switch
    {
        BaseUnit.Gram       => "500g, 2kg or 8x250g",
        BaseUnit.Millilitre => "330ml, 2L or 6x375ml",
        _                   => "12 or 6x2",
    };

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // A decimal holds at most 28 places; '#' drops the trailing zeros.
    private static string Exact(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

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
