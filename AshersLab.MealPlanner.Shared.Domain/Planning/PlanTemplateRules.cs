using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// A plan template - "the 4-week rotation" - as the op log sees it: a name and how many weeks it runs before it
/// repeats. What is eaten when is its entries (<see cref="TemplateEntryRules"/>), rows of their own.
/// </summary>
public static class PlanTemplateRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "PlanTemplate";

    public const string NameField = "Name";

    /// <summary>Weeks before it repeats (<see cref="OpValues.FormatInt"/>), 1 to <see cref="MaxWeeks"/>.</summary>
    public const string WeekCountField = "WeekCount";

    public const int MaxWeeks = 12;

    public const string Noun = "A plan";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField, WeekCountField };

    private static readonly string[] RequiredOnCreate = [NameField, WeekCountField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? WeekCountProblem(int weeks) => weeks switch
    {
        < 1        => "A plan runs for at least one week.",
        > MaxWeeks => $"Keep it to {MaxWeeks} weeks.",
        _          => null,
    };

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField      => Names.FieldProblem(field.Value, Noun),
            WeekCountField => OpValues.TryParseInt(field.Value, out int weeks) ? WeekCountProblem(weeks) : "The weeks are not a whole number.",
            _              => null,
        });
}
