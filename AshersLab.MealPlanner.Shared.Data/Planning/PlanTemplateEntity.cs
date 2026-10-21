using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="PlanTemplate"/> row.</summary>
public sealed class PlanTemplateEntity : SyncedEntity<PlanTemplate>
{
    public override string Entity => PlanTemplateRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => PlanTemplateRules.OpProblem(kind, fields);

    protected override void Set(PlanTemplate row, OpField field)
    {
        switch (field.Name)
        {
            case PlanTemplateRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case PlanTemplateRules.WeekCountField:
                row.WeekCount = OpValues.TryParseInt(field.Value, out int weeks) ? weeks : throw Unreadable(field);
                break;
            case PlanTemplateRules.StartsOnField:
                row.StartsOn = field.Value is null ? null : OpValues.TryParseDate(field.Value, out DateOnly starts) ? starts : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A plan has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(PlanTemplate row) =>
    [
        new(PlanTemplateRules.NameField, row.Name),
        new(PlanTemplateRules.WeekCountField, OpValues.FormatInt(row.WeekCount)),
        new(PlanTemplateRules.StartsOnField, row.StartsOn is { } starts ? OpValues.FormatDate(starts) : null),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
