using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// One thing eaten in a plan: a week of the template, a day, a slot, who eats it, and - as rows of their own
/// (<see cref="TemplateChoiceRules"/>) - the meals it may be. A slot can hold several entries: "Rice cakes, cheese, apple"
/// is three, and the toddler's lunch beside the adults' is another, for fewer people.
/// </summary>
public static class TemplateEntryRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "TemplateEntry";

    /// <summary>The template's id. Required, never null, never changed by the pages.</summary>
    public const string TemplateField = "TemplateId";

    /// <summary>Which week of the template, from 1 (<see cref="OpValues.FormatInt"/>).</summary>
    public const string WeekField = "Week";

    /// <summary>A <see cref="DayOfWeek"/> by name: "Monday".</summary>
    public const string DayField = "Day";

    /// <summary>The slot's id. Required, never null.</summary>
    public const string SlotField = "SlotId";

    /// <summary>
    /// Who eats it: null for everyone - so someone added to the household later is in it too - or some people
    /// (<see cref="PlanPeople.Format"/>). Set as a whole: who eats a meal is one decision, so the later edit wins.
    /// </summary>
    public const string PeopleField = "People";

    public const string Noun = "A plan entry";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        TemplateField, WeekField, DayField, SlotField, PeopleField,
    };

    private static readonly string[] RequiredOnCreate = [TemplateField, WeekField, DayField, SlotField];

    /// <summary>
    /// A week beyond the template's <c>WeekCount</c> is accepted: the count is another row's field, and the template may
    /// have been shortened on another device. Such entries are kept and shown nowhere, and come back if it grows again.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            TemplateField => OpValues.TryParseId(field.Value, out Guid? template) && template is not null ? null : "It needs a plan.",
            WeekField     => OpValues.TryParseInt(field.Value, out int week) && week >= 1 && week <= PlanTemplateRules.MaxWeeks
                ? null : "The week is not one a plan has.",
            DayField      => PlanDays.TryParse(field.Value, out _) ? null : "The day is not a day of the week.",
            SlotField     => OpValues.TryParseId(field.Value, out Guid? slot) && slot is not null ? null : "It needs a slot.",
            PeopleField   => PlanPeople.TryParse(field.Value, out _) ? null : "The people are not a list of people.",
            _             => null,
        });
}

/// <summary>The days of a plan's week, Monday first, as the planner's sheet has them.</summary>
public static class PlanDays
{
    public static readonly IReadOnlyList<DayOfWeek> Week =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
        DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static string Format(DayOfWeek day) => day.ToString();

    /// <summary>Reads exactly a day's name, case and all - not a number.</summary>
    public static bool TryParse(string? value, out DayOfWeek day)
    {
        foreach (DayOfWeek candidate in Week)
        {
            if (candidate.ToString() == value)
            {
                day = candidate;
                return true;
            }
        }
        day = default;
        return false;
    }

    /// <summary>"Mon", for a column heading or a chip.</summary>
    public static string Short(DayOfWeek day) => day.ToString()[..3];

    /// <summary>A day's name as the sheet may write it - "Monday", "mon", "Tues" - or null.</summary>
    public static DayOfWeek? Read(string text)
    {
        string word = Names.Normalise(text).ToLowerInvariant();
        return word.Length < 3 ? null
            : Week.Select(d => (DayOfWeek?)d).FirstOrDefault(d => d.ToString()!.ToLowerInvariant().StartsWith(word, StringComparison.Ordinal));
    }
}

/// <summary>
/// Who eats a plan entry, on the wire: null for everyone, otherwise the people's ids, lower-case, sorted and
/// comma-separated - one spelling for one set, so the same choice made on two phones is the same text.
/// </summary>
public static class PlanPeople
{
    /// <summary>More than any household; it bounds the column (<c>SyncedEntities.BuildModel</c>).</summary>
    public const int Max = 20;

    /// <summary>Null (everyone) for null or an empty set: an entry nobody eats is not an entry.</summary>
    public static string? Format(IEnumerable<Guid>? people)
    {
        if (people is null)
            return null;
        List<string> ids = [.. people.Distinct().Select(p => p.ToString("D")).Order(StringComparer.Ordinal)];
        return ids.Count == 0 ? null : string.Join(',', ids);
    }

    /// <summary>Reads <see cref="Format"/>'s output, and only that. Null is everyone.</summary>
    public static bool TryParse(string? value, out IReadOnlyList<Guid>? people)
    {
        people = null;
        if (value is null)
            return true;
        List<Guid> ids = [];
        foreach (string part in value.Split(','))
        {
            if (!OpValues.TryParseId(part, out Guid? id) || id is not { } person)
                return false;
            ids.Add(person);
        }
        if (ids.Count > Max || Format(ids) != value)
            return false;
        people = ids;
        return true;
    }
}
