using System.Globalization;

namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// The text an op carries for the field types every entity shares, and the one way to read each back. Each format
/// has exactly one spelling, so two ops that mean the same value carry the same text.
/// </summary>
public static class OpValues
{
    /// <summary>A reference to another row, or null for none. Lower-case, hyphenated.</summary>
    public static string? FormatId(Guid? id) => id?.ToString("D");

    public static bool TryParseId(string? value, out Guid? id)
    {
        id = null;
        if (value is null)
            return true;
        if (!Guid.TryParseExact(value, "D", out Guid parsed) || parsed == Guid.Empty || parsed.ToString("D") != value)
            return false;
        id = parsed;
        return true;
    }

    /// <summary>
    /// A quantity: invariant culture, no trailing zeros ("2000", "1.5", never "2000.0"). Decimal keeps the scale it
    /// was computed with, so without this the same quantity could travel as two different texts.
    /// </summary>
    public static string FormatDecimal(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Reads <see cref="FormatDecimal"/>'s output, and only that.</summary>
    public static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result)
        && FormatDecimal(result) == value;

    /// <summary>Whole cents, as plain digits ("2499"). Money is never a decimal on the wire.</summary>
    public static string FormatCents(long cents) => cents.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads <see cref="FormatCents"/>'s output, and only that: no "+", no leading zeros, no decimal point.</summary>
    public static bool TryParseCents(string? value, out long cents) =>
        long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out cents)
        && FormatCents(cents) == value;

    /// <summary>A calendar date, "2026-10-21". No time and no zone: a price is seen on a day.</summary>
    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static string FormatBool(bool value) => value ? "true" : "false";

    public static bool TryParseBool(string? value, out bool result)
    {
        result = value == "true";
        return value is "true" or "false";
    }
}
