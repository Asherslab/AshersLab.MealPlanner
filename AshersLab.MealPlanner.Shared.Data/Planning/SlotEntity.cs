using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="Slot"/> row.</summary>
public sealed class SlotEntity : SyncedEntity<Slot>
{
    public override string Entity => SlotRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => SlotRules.OpProblem(kind, fields);

    protected override void Set(Slot row, OpField field)
    {
        switch (field.Name)
        {
            case SlotRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case SlotRules.PositionField:
                row.Position = ListPosition.TryParse(field.Value, out decimal position)
                    ? position
                    : throw new ArgumentException($"'{field.Value}' is not a position.", nameof(field));
                break;
            default:
                throw new ArgumentException($"A slot has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Slot row) =>
    [
        new(SlotRules.NameField, row.Name),
        new(SlotRules.PositionField, ListPosition.Format(row.Position)),
    ];
}
