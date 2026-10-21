using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Meals;

/// <summary>How an op's fields map onto a <see cref="MealGroup"/> row.</summary>
public sealed class MealGroupEntity : SyncedEntity<MealGroup>
{
    public override string Entity => MealGroupRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => MealGroupRules.OpProblem(kind, fields);

    protected override void Set(MealGroup row, OpField field)
    {
        switch (field.Name)
        {
            case MealGroupRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case MealGroupRules.PositionField:
                row.Position = ListPosition.TryParse(field.Value, out decimal position)
                    ? position
                    : throw new ArgumentException($"'{field.Value}' is not a position.", nameof(field));
                break;
            default:
                throw new ArgumentException($"A meal group has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(MealGroup row) =>
    [
        new(MealGroupRules.NameField, row.Name),
        new(MealGroupRules.PositionField, ListPosition.Format(row.Position)),
    ];

    /// <summary>The list's order: position, then name for equal positions, then id so it is the same everywhere.</summary>
    public static IEnumerable<MealGroup> InOrder(IEnumerable<MealGroup> groups) =>
        groups.OrderBy(g => g.Position).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Id);
}
