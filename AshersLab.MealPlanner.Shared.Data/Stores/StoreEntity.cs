using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Stores;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Stores;

/// <summary>How an op's fields map onto a <see cref="Store"/> row.</summary>
public sealed class StoreEntity : SyncedEntity<Store>
{
    public override string Entity => StoreRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => StoreRules.OpProblem(kind, fields);

    protected override void Set(Store row, OpField field)
    {
        switch (field.Name)
        {
            case StoreRules.NameField:
                row.Name = field.Value ?? "";
                break;
            default:
                throw new ArgumentException($"A store has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Store row) => [new(StoreRules.NameField, row.Name)];
}
