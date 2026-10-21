namespace AshersLab.MealPlanner.Shared.Domain;

/// <summary>
/// What every named thing in the catalogue (ingredient, store, category) accepts as a name. One rule, so the
/// page, the browser's op writer and the server's push agree on all of them.
/// </summary>
public static class Names
{
    public const int MaxLength = 100;

    /// <summary>Trims and collapses inner runs of whitespace: " beef  mince " is "beef mince".</summary>
    public static string Normalise(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null when <paramref name="name"/> (already normalised) is acceptable; otherwise why not.</summary>
    /// <param name="noun">"An ingredient", "A store" - what the message says needs a name.</param>
    public static string? Problem(string name, string noun) => name switch
    {
        { Length: 0 }             => $"{noun} needs a name.",
        { Length: > MaxLength }   => $"Keep the name to {MaxLength} characters.",
        _                         => null,
    };

    /// <summary>The check on a name an op carries: present, already normalised, acceptable.</summary>
    public static string? FieldProblem(string? value, string noun) =>
        value is null || Normalise(value) != value || Problem(value, noun) is not null
            ? Problem(Normalise(value), noun) ?? "The name is not normalised."
            : null;

    /// <summary>
    /// Whether two names are the same to the person reading them - "Costco" and "costco". For warning about a
    /// duplicate, never for identity: two devices can each add a "Costco" offline, and both rows survive.
    /// </summary>
    public static bool Same(string a, string b) => string.Equals(Normalise(a), Normalise(b), StringComparison.OrdinalIgnoreCase);
}
