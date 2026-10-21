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

    /// <summary>
    /// The Monday its week 1 began on the calendar (<see cref="OpValues.FormatDate"/>), or null before it was first applied.
    /// One decision for the household, so the later edit wins; see <see cref="Rotation"/>.
    /// </summary>
    public const string StartsOnField = "StartsOn";

    /// <summary>
    /// The rotations it had before its start or week count last changed, each until the day it changed
    /// (<see cref="RotationPhases.Format"/>); null for none. Sent with every such change, in the same op.
    /// </summary>
    public const string EarlierField = "Earlier";

    public const int MaxWeeks = 12;

    public const string Noun = "A plan";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField, WeekCountField, StartsOnField, EarlierField };

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
            StartsOnField  => field.Value is null || (OpValues.TryParseDate(field.Value, out DateOnly starts) && starts.DayOfWeek == DayOfWeek.Monday)
                ? null : "A plan's week 1 starts on a Monday.",
            EarlierField   => RotationPhases.TryParse(field.Value, out _) ? null : "The plan's earlier rotations are not readable.",
            _              => null,
        });

    /// <summary>
    /// The fields that change a plan's rotation from <paramref name="from"/> on - to <paramref name="startsOn"/> and
    /// <paramref name="weekCount"/> - keeping every day before it as it was, and replacing any later change. Empty when the
    /// rotation on <paramref name="from"/> is that already: filling an older week as the week it was moves nothing.
    /// </summary>
    public static IReadOnlyList<OpField> RotationChange(string? earlier, DateOnly? currentStart, int currentWeeks,
                                                        DateOnly from, DateOnly startsOn, int weekCount)
    {
        IReadOnlyList<RotationPhase> phases = RotationPhases.TryParse(earlier, out IReadOnlyList<RotationPhase> read) ? read : [];
        if (RotationPhases.On(phases, currentStart, currentWeeks, from) == (startsOn, weekCount))
            return [];

        List<OpField> fields = [];
        if (currentStart != startsOn)
            fields.Add(new OpField(StartsOnField, OpValues.FormatDate(startsOn)));
        if (currentWeeks != weekCount)
            fields.Add(new OpField(WeekCountField, OpValues.FormatInt(weekCount)));
        string? after = RotationPhases.Format(RotationPhases.Change(phases, currentStart, currentWeeks, from, startsOn, weekCount));
        if (after != earlier)
            fields.Add(new OpField(EarlierField, after));
        return fields;
    }
}
