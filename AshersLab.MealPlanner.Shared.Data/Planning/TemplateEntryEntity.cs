using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="TemplateEntry"/> row.</summary>
public sealed class TemplateEntryEntity : SyncedEntity<TemplateEntry>
{
    public override string Entity => TemplateEntryRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => TemplateEntryRules.OpProblem(kind, fields);

    protected override void Set(TemplateEntry row, OpField field)
    {
        switch (field.Name)
        {
            case TemplateEntryRules.TemplateField:
                row.TemplateId = OpValues.TryParseId(field.Value, out Guid? template) && template is { } templateId ? templateId : throw Unreadable(field);
                break;
            case TemplateEntryRules.WeekField:
                row.Week = OpValues.TryParseInt(field.Value, out int week) ? week : throw Unreadable(field);
                break;
            case TemplateEntryRules.DayField:
                row.Day = PlanDays.TryParse(field.Value, out DayOfWeek day) ? day : throw Unreadable(field);
                break;
            case TemplateEntryRules.SlotField:
                row.SlotId = OpValues.TryParseId(field.Value, out Guid? slot) && slot is { } slotId ? slotId : throw Unreadable(field);
                break;
            case TemplateEntryRules.PeopleField:
                row.People = PlanPeople.TryParse(field.Value, out _) ? field.Value : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A plan entry has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(TemplateEntry row) =>
    [
        new(TemplateEntryRules.TemplateField, OpValues.FormatId(row.TemplateId)),
        new(TemplateEntryRules.WeekField, OpValues.FormatInt(row.Week)),
        new(TemplateEntryRules.DayField, PlanDays.Format(row.Day)),
        new(TemplateEntryRules.SlotField, OpValues.FormatId(row.SlotId)),
        new(TemplateEntryRules.PeopleField, row.People),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
