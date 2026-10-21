using System.Globalization;
using System.Text.RegularExpressions;

namespace AshersLab.MealPlanner.Shared.Domain.Prices;

/// <summary>
/// Money as the planner types and reads it. Always whole cents (<c>long</c>); never a <c>double</c>, and never a
/// <c>decimal</c> outside the moment of parsing.
/// </summary>
public static partial class Money
{
    /// <summary>$100,000: nothing in a grocery shop costs more. Anything bigger is a typo.</summary>
    public const long MaxCents = 10_000_000;

    // "$24.99", "24.99", "24.9", "24", ".99", "$ 3". No thousands separators: "1,50" would be a typo read silently.
    [GeneratedRegex(@"^\s*\$?\s*(?<dollars>\d*)(?:\.(?<cents>\d{0,2}))?\s*$")]
    private static partial Regex Shape();

    /// <summary>Whole cents for what was typed; null with a reason in <paramref name="problem"/> when it cannot be read.</summary>
    public static long? ParseCents(string? text, out string? problem)
    {
        problem = null;
        Match match = Shape().Match(text ?? "");
        string dollars = match.Groups["dollars"].Value;
        string cents   = match.Groups["cents"].Value;
        if (!match.Success || dollars.Length + cents.Length == 0)
        {
            problem = string.IsNullOrWhiteSpace(text) ? "Enter the price." : "Type a price like 24.99.";
            return null;
        }

        if (dollars.Length > 9)
        {
            problem = "That is more than anything costs.";
            return null;
        }

        long value = (dollars.Length == 0 ? 0 : long.Parse(dollars, CultureInfo.InvariantCulture)) * 100
                     + (cents.Length == 0 ? 0 : long.Parse(cents.PadRight(2, '0'), CultureInfo.InvariantCulture));

        problem = CentsProblem(value);
        return problem is null ? value : null;
    }

    /// <summary>Null when <paramref name="cents"/> is a price; otherwise why not. Free is not a price.</summary>
    public static string? CentsProblem(long cents) => cents switch
    {
        <= 0       => "A price has to be more than $0.",
        > MaxCents => "That is more than anything costs.",
        _          => null,
    };

    /// <summary>"$24.99", "$0.05", "$1234.00" - no thousands separator, as it is typed.</summary>
    public static string Format(long cents) =>
        (cents < 0 ? "-$" : "$") + (Math.Abs(cents) / 100).ToString(CultureInfo.InvariantCulture)
        + "." + (Math.Abs(cents) % 100).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>What goes back in the box for editing: "24.99".</summary>
    public static string Editable(long cents) => Format(cents)[1..];
}
