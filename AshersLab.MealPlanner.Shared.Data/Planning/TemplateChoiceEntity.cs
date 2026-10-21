using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Planning;

/// <summary>How an op's fields map onto a <see cref="TemplateChoice"/> row.</summary>
public sealed class TemplateChoiceEntity : SyncedEntity<TemplateChoice>
{
    public override string Entity => TemplateChoiceRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => TemplateChoiceRules.OpProblem(kind, fields);

    protected override void Set(TemplateChoice row, OpField field)
    {
        switch (field.Name)
        {
            case TemplateChoiceRules.EntryField:
                row.EntryId = OpValues.TryParseId(field.Value, out Guid? entry) && entry is { } entryId ? entryId : throw Unreadable(field);
                break;
            case TemplateChoiceRules.MealField:
                row.MealId = OpValues.TryParseId(field.Value, out Guid? meal) && meal is { } mealId ? mealId : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A plan choice has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(TemplateChoice row) =>
    [
        new(TemplateChoiceRules.EntryField, OpValues.FormatId(row.EntryId)),
        new(TemplateChoiceRules.MealField, OpValues.FormatId(row.MealId)),
    ];

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
