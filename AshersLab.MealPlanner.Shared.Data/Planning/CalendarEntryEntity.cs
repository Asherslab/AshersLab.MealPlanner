using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="CalendarEntry"/> row.</summary>
public sealed class CalendarEntryEntity : SyncedEntity<CalendarEntry>
{
    public override string Entity => CalendarEntryRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => CalendarEntryRules.OpProblem(kind, fields);

    protected override void Set(CalendarEntry row, OpField field)
    {
        switch (field.Name)
        {
            case CalendarEntryRules.TemplateEntryField:
                row.TemplateEntryId = OpValues.TryParseId(field.Value, out Guid? entry) ? entry : throw Unreadable(field);
                break;
            case CalendarEntryRules.DateField:
                row.Date = OpValues.TryParseDate(field.Value, out DateOnly date) ? date : throw Unreadable(field);
                break;
            case CalendarEntryRules.SlotField:
                row.SlotId = OpValues.TryParseId(field.Value, out Guid? slot) && slot is { } slotId ? slotId : throw Unreadable(field);
                break;
            case CalendarEntryRules.MealField:
                row.MealId = OpValues.TryParseId(field.Value, out Guid? meal) ? meal : throw Unreadable(field);
                break;
            case CalendarEntryRules.PeopleField:
                row.People = CalendarPeople.TryParse(field.Value, out _) ? field.Value : throw Unreadable(field);
                break;
            case CalendarEntryRules.StatusField:
                row.Status = CalendarEntryRules.TryParseStatus(field.Value, out CalendarStatus status) ? status : throw Unreadable(field);
                break;
            case CalendarEntryRules.MovedDateField:
                row.MovedDate = field.Value is null ? null : OpValues.TryParseDate(field.Value, out DateOnly moved) ? moved : throw Unreadable(field);
                break;
            case CalendarEntryRules.MovedSlotField:
                row.MovedSlotId = OpValues.TryParseId(field.Value, out Guid? movedSlot) ? movedSlot : throw Unreadable(field);
                break;
            case CalendarEntryRules.EatsFromField:
                row.EatsFrom = OpValues.TryParseId(field.Value, out Guid? batch) ? batch : throw Unreadable(field);
                break;
            case CalendarEntryRules.ServesMadeField:
                row.ServesMade = field.Value is null ? null : OpValues.TryParseInt(field.Value, out int made) ? made : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A calendar entry has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(CalendarEntry row) =>
    [
        new(CalendarEntryRules.TemplateEntryField, OpValues.FormatId(row.TemplateEntryId)),
        new(CalendarEntryRules.DateField, OpValues.FormatDate(row.Date)),
        new(CalendarEntryRules.SlotField, OpValues.FormatId(row.SlotId)),
        new(CalendarEntryRules.MealField, OpValues.FormatId(row.MealId)),
        new(CalendarEntryRules.PeopleField, row.People),
        new(CalendarEntryRules.StatusField, CalendarEntryRules.FormatStatus(row.Status)),
        new(CalendarEntryRules.MovedDateField, row.MovedDate is { } moved ? OpValues.FormatDate(moved) : null),
        new(CalendarEntryRules.MovedSlotField, OpValues.FormatId(row.MovedSlotId)),
        new(CalendarEntryRules.EatsFromField, OpValues.FormatId(row.EatsFrom)),
        new(CalendarEntryRules.ServesMadeField, row.ServesMade is { } made ? OpValues.FormatInt(made) : null),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
