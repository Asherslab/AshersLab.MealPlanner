using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Planning;

/// <summary>
/// Whether a calendar entry happened. <b>A past entry still <see cref="Planned"/> counts as eaten</b> - nobody ticks off
/// every meal; skipping ("we didn't have it") is the one thing recorded. On the wire by name - never rename.
/// </summary>
public enum CalendarStatus
{
    Planned,
    Skipped,
}

/// <summary>
/// One thing eaten on a real date, in a slot, by everyone or some people - made by applying a plan (it remembers its
/// template entry) or added by hand. As the op log sees it.
/// </summary>
/// <remarks>
/// <b>A create carries only what never changes</b> - the template entry, and the date and slot it was planned for. Two
/// devices applying a plan create the same id (<see cref="Rotation.EntryId"/>) with the same fields, so the later create
/// changes nothing: if it carried the meal, the status or where it is, a stale device's apply would clock after a skip or
/// a move made elsewhere and undo it. Everything the planner changes is a field of its own, null meaning "as planned",
/// and saving sends only the fields changed (<see cref="CalendarEntryDraft.ChangesFrom"/>):
/// <list type="bullet">
/// <item><see cref="MealField"/> - null eats the template entry's usual choice, whatever it is now.</item>
/// <item><see cref="PeopleField"/> - null is the template entry's people (everyone, for an entry added by hand).</item>
/// <item><see cref="MovedDateField"/> and <see cref="MovedSlotField"/> - null is where it was planned. Set together.</item>
/// </list>
/// </remarks>
public static class CalendarEntryRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "CalendarEntry";

    /// <summary>The template entry it was made from; absent for one added by hand. Never changed.</summary>
    public const string TemplateEntryField = "TemplateEntryId";

    /// <summary>The date it was planned for (<see cref="OpValues.FormatDate"/>). Required; never changed - a move is <see cref="MovedDateField"/>.</summary>
    public const string DateField = "Date";

    /// <summary>The slot it was planned for. Required; never changed - a move is <see cref="MovedSlotField"/>.</summary>
    public const string SlotField = "SlotId";

    /// <summary>The meal, when it is not the template entry's usual one: another of its choices, or any meal. Null: the usual one.</summary>
    public const string MealField = "MealId";

    /// <summary><see cref="CalendarPeople"/>: null as planned, or who eats it this time.</summary>
    public const string PeopleField = "People";

    /// <summary>A <see cref="CalendarStatus"/> by name. A new entry is <see cref="CalendarStatus.Planned"/>.</summary>
    public const string StatusField = "Status";

    /// <summary>Where it was moved to, or null where it was planned. Always sent with <see cref="MovedSlotField"/>: a move is one decision.</summary>
    public const string MovedDateField = "MovedDate";

    public const string MovedSlotField = "MovedSlotId";

    /// <summary>A leftovers entry's batch - the cooked calendar entry it eats from; null eats whatever is left (<see cref="Serves"/>).</summary>
    public const string EatsFromField = "EatsFrom";

    /// <summary>A cooked entry's serves made, when not the meal's serves per batch (<see cref="OpValues.FormatInt"/>, 1 to 99).</summary>
    public const string ServesMadeField = "ServesMade";

    public const int MaxServes = 99;

    public const string Noun = "A calendar entry";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        TemplateEntryField, DateField, SlotField, MealField, PeopleField, StatusField, MovedDateField, MovedSlotField,
        EatsFromField, ServesMadeField,
    };

    private static readonly string[] RequiredOnCreate = [DateField, SlotField];

    public static string FormatStatus(CalendarStatus status) => status.ToString();

    public static bool TryParseStatus(string? value, out CalendarStatus status)
    {
        foreach (CalendarStatus candidate in Enum.GetValues<CalendarStatus>())
        {
            if (candidate.ToString() == value)
            {
                status = candidate;
                return true;
            }
        }
        status = default;
        return false;
    }

    /// <summary>Whether the template, its entry, the meal and the slots exist is not checked: any may not have arrived yet, or be gone.</summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            TemplateEntryField => OpValues.TryParseId(field.Value, out _) ? null : "The plan entry is not an id.",
            DateField          => OpValues.TryParseDate(field.Value, out _) ? null : "It needs a date.",
            SlotField          => OpValues.TryParseId(field.Value, out Guid? slot) && slot is not null ? null : "It needs a slot.",
            MealField          => OpValues.TryParseId(field.Value, out _) ? null : "The meal is not an id.",
            PeopleField        => CalendarPeople.TryParse(field.Value, out _) ? null : "The people are not a list of people.",
            StatusField        => TryParseStatus(field.Value, out _) ? null : "The status is not Planned or Skipped.",
            MovedDateField     => field.Value is null || OpValues.TryParseDate(field.Value, out _) ? null : "The date it moved to is not a date.",
            MovedSlotField     => OpValues.TryParseId(field.Value, out _) ? null : "The slot it moved to is not an id.",
            EatsFromField      => OpValues.TryParseId(field.Value, out _) ? null : "The batch it eats from is not an id.",
            ServesMadeField    => field.Value is null ? null
                : OpValues.TryParseInt(field.Value, out int made) ? ServesMadeProblem(made) : "The serves made are not a whole number.",
            _                  => null,
        });

    /// <summary>The fields creating an entry a plan made: only what never changes (see the remarks above).</summary>
    public static IReadOnlyList<OpField> CreateFields(GeneratedEntry entry) =>
    [
        new(TemplateEntryField, OpValues.FormatId(entry.TemplateEntryId)),
        new(DateField, OpValues.FormatDate(entry.Date)),
        new(SlotField, OpValues.FormatId(entry.SlotId)),
    ];

    /// <summary>Whether the planner has changed anything about an entry - a meal, people, a skip or a move.</summary>
    public static bool Touched(Guid? meal, string? people, CalendarStatus status, DateOnly? movedDate, Guid? movedSlot,
                               Guid? eatsFrom = null, int? servesMade = null) =>
        meal is not null || people is not null || status != CalendarStatus.Planned || movedDate is not null || movedSlot is not null
        || eatsFrom is not null || servesMade is not null;

    /// <summary>
    /// What the entry sheet saves as a cooked entry's serves made. Typing the meal's own number clears it - it follows the
    /// meal again - but <b>only when the box was edited</b>: an untouched box keeps what the entry held, even when that
    /// equals the meal's number. Otherwise saving anything else on the entry would send a <c>ServesMade = null</c> nobody
    /// made, and its newer clock would erase a number set on another phone.
    /// </summary>
    public static int? ServesMadeToSave(int? held, int? typed, int mealServes) =>
        typed == held ? held : typed == mealServes ? null : typed;

    public static string? ServesMadeProblem(int made) => made switch
    {
        < 1         => "A batch makes at least one serve.",
        > MaxServes => $"Keep it to {MaxServes} serves.",
        _           => null,
    };
}

/// <summary>
/// Who eats a calendar entry, on the wire: null as planned (the template entry's people; everyone for an entry added by
/// hand), <see cref="Everyone"/>, or <see cref="PlanPeople.Format"/>'s list. "Everyone" needs its own word because null
/// already means "as planned", and a plan entry for the toddler can be eaten by everyone this once.
/// </summary>
public static class CalendarPeople
{
    public const string Everyone = "everyone";

    /// <summary>Reads null, <see cref="Everyone"/> or a <see cref="PlanPeople"/> list; <paramref name="people"/> is null for everyone and as planned alike.</summary>
    public static bool TryParse(string? value, out IReadOnlyList<Guid>? people)
    {
        people = null;
        return value is null or Everyone || (PlanPeople.TryParse(value, out people) && people is not null);
    }

    /// <summary>Who eats it: the field read over <paramref name="planned"/> (the template entry's people, null for everyone).</summary>
    public static IReadOnlyList<Guid>? Resolve(string? field, IReadOnlyList<Guid>? planned) => field switch
    {
        null     => planned,
        Everyone => null,
        _        => PlanPeople.TryParse(field, out IReadOnlyList<Guid>? people) ? people : planned,
    };

    /// <summary>
    /// The field for <paramref name="wanted"/> (null: everyone) over <paramref name="planned"/>: null when it is what was
    /// planned, so a later change to the plan still reaches it.
    /// </summary>
    public static string? Field(IReadOnlyList<Guid>? wanted, IReadOnlyList<Guid>? planned)
    {
        string? want = PlanPeople.Format(wanted);
        return want == PlanPeople.Format(planned) ? null : want ?? Everyone;
    }
}

/// <summary>A calendar entry as it is now, as the entry sheet's diff reads it.</summary>
/// <param name="Usual">The template entry's usual meal; null for an entry added by hand, or a plan entry with no choices left.</param>
/// <param name="PlannedPeople">The template entry's people (null: everyone); null for an entry added by hand.</param>
public sealed record CalendarEntryNow(
    bool              FromPlan,
    Guid?             Usual,
    IReadOnlyList<Guid>? PlannedPeople,
    Guid?             MealField,
    string?           PeopleField,
    CalendarStatus    Status,
    DateOnly          Date,
    Guid              Slot,
    DateOnly?         MovedDate,
    Guid?             MovedSlot,
    Guid?             EatsFrom = null,
    int?              ServesMade = null
)
{
    public Guid? Meal => MealField ?? (FromPlan ? Usual : null);

    public IReadOnlyList<Guid>? People => CalendarPeople.Resolve(PeopleField, FromPlan ? PlannedPeople : null);

    public DateOnly Day => MovedDate ?? Date;

    public Guid InSlot => MovedSlot ?? Slot;
}

/// <summary>
/// A calendar entry as the entry sheet answers it: the meal, who eats it, skipped or not, and where - and for a batch, how
/// many serves it made (null: the meal's), for leftovers which batch it eats (null: whatever is left).
/// </summary>
public sealed record CalendarEntryDraft(Guid? Meal, IReadOnlyList<Guid>? People, bool Skipped, DateOnly Day, Guid Slot,
                                        Guid? EatsFrom = null, int? ServesMade = null)
{
    /// <summary>
    /// What saving this over <paramref name="now"/> sends - <b>only the fields the planner changed</b>, since an op that
    /// changes nothing still wins its merge. A skip or a move sends its own fields and never the meal or people: phone A
    /// choosing soup while an offline phone B skips the same entry must end as skipped soup, not as B's stale noodles. The
    /// price: a skipped or moved entry still follows its plan entry, so one whose plan entry is removed has no meal ("meal
    /// removed") until one is chosen.
    /// <para>
    /// A meal or people chosen back to what the plan says sends null, so the entry follows the plan again. A move sends both
    /// its fields, and moving home clears them; replanning a skipped entry is moving it and un-skipping it in one op.
    /// </para>
    /// </summary>
    public IReadOnlyList<OpField> ChangesFrom(CalendarEntryNow now)
    {
        List<OpField> fields = [];

        CalendarStatus status = Skipped ? CalendarStatus.Skipped : CalendarStatus.Planned;
        bool home = Day == now.Date && Slot == now.Slot;

        if (Meal != now.Meal)
        {
            Guid? meal = now.FromPlan && Meal == now.Usual ? null : Meal;
            if (meal != now.MealField)
                fields.Add(new OpField(CalendarEntryRules.MealField, OpValues.FormatId(meal)));
        }

        if (PlanPeople.Format(People) != PlanPeople.Format(now.People))
        {
            string? people = CalendarPeople.Field(People, now.FromPlan ? now.PlannedPeople : null);
            if (people != now.PeopleField)
                fields.Add(new OpField(CalendarEntryRules.PeopleField, people));
        }

        if (status != now.Status)
            fields.Add(new OpField(CalendarEntryRules.StatusField, CalendarEntryRules.FormatStatus(status)));

        (DateOnly? Date, Guid? Slot) moved = home ? (null, null) : (Day, Slot);
        if (moved.Date != now.MovedDate || moved.Slot != now.MovedSlot)
        {
            fields.Add(new OpField(CalendarEntryRules.MovedDateField, moved.Date is { } d ? OpValues.FormatDate(d) : null));
            fields.Add(new OpField(CalendarEntryRules.MovedSlotField, OpValues.FormatId(moved.Slot)));
        }

        if (EatsFrom != now.EatsFrom)
            fields.Add(new OpField(CalendarEntryRules.EatsFromField, OpValues.FormatId(EatsFrom)));
        if (ServesMade != now.ServesMade)
            fields.Add(new OpField(CalendarEntryRules.ServesMadeField, ServesMade is { } made ? OpValues.FormatInt(made) : null));
        return fields;
    }
}
