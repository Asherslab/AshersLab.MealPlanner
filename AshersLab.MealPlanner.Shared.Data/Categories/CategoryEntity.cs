using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Categories;

/// <summary>How an op's fields map onto a <see cref="Category"/> row.</summary>
public sealed class CategoryEntity : SyncedEntity<Category>
{
    public override string Entity => CategoryRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => CategoryRules.OpProblem(kind, fields);

    protected override void Set(Category row, OpField field)
    {
        switch (field.Name)
        {
            case CategoryRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case CategoryRules.PositionField:
                row.Position = ListPosition.TryParse(field.Value, out decimal position)
                    ? position
                    : throw new ArgumentException($"'{field.Value}' is not a position.", nameof(field));
                break;
            default:
                throw new ArgumentException($"A category has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Category row) =>
    [
        new(CategoryRules.NameField, row.Name),
        new(CategoryRules.PositionField, ListPosition.Format(row.Position)),
    ];

    /// <summary>The list's order: position, then name for equal positions, then id so it is the same everywhere.</summary>
    public static IEnumerable<Category> InOrder(IEnumerable<Category> categories) =>
        categories.OrderBy(c => c.Position).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id);
}
