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

    public static string FormatBool(bool value) => value ? "true" : "false";

    public static bool TryParseBool(string? value, out bool result)
    {
        result = value == "true";
        return value is "true" or "false";
    }
}
