using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>How an op's fields map onto a <see cref="Meal"/> row.</summary>
public sealed class MealEntity : SyncedEntity<Meal>
{
    public override string Entity => MealRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => MealRules.OpProblem(kind, fields);

    protected override void Set(Meal row, OpField field)
    {
        switch (field.Name)
        {
            case MealRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case MealRules.GroupField:
                row.GroupId = OpValues.TryParseId(field.Value, out Guid? group) ? group : throw Unreadable(field);
                break;
            case MealRules.KindField:
                row.Kind = MealKinds.TryParse(field.Value, out MealKind kind) ? kind : throw Unreadable(field);
                break;
            case MealRules.ServesPerBatchField:
                row.ServesPerBatch = OpValues.TryParseInt(field.Value, out int serves) ? serves : throw Unreadable(field);
                break;
            case MealRules.FixedCostCentsField:
                row.FixedCostCents = field.Value is null ? null
                    : OpValues.TryParseCents(field.Value, out long cents) ? cents
                    : throw Unreadable(field);
                break;
            case MealRules.NotesField:
                row.Notes = field.Value;
                break;
            default:
                throw new ArgumentException($"A meal has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Meal row) =>
    [
        new(MealRules.NameField, row.Name),
        new(MealRules.GroupField, OpValues.FormatId(row.GroupId)),
        new(MealRules.KindField, MealKinds.Format(row.Kind)),
        new(MealRules.ServesPerBatchField, OpValues.FormatInt(row.ServesPerBatch)),
        new(MealRules.FixedCostCentsField, row.FixedCostCents is { } cents ? OpValues.FormatCents(cents) : null),
        new(MealRules.NotesField, row.Notes),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
