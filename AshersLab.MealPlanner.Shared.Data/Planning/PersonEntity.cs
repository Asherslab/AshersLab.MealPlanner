using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="Person"/> row.</summary>
public sealed class PersonEntity : SyncedEntity<Person>
{
    public override string Entity => PersonRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => PersonRules.OpProblem(kind, fields);

    protected override void Set(Person row, OpField field)
    {
        switch (field.Name)
        {
            case PersonRules.NameField:
                row.Name = field.Value ?? "";
                break;
            case PersonRules.ColourField:
                row.Colour = field.Value ?? "";
                break;
            default:
                throw new ArgumentException($"A person has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(Person row) =>
    [
        new(PersonRules.NameField, row.Name),
        new(PersonRules.ColourField, row.Colour),
    ];
}
