using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Shopping;

/// <summary>How an op's fields map onto a <see cref="ShoppingList"/> row.</summary>
public sealed class ShoppingListEntity : SyncedEntity<ShoppingList>
{
    public override string Entity => ShoppingListRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ShoppingListRules.OpProblem(kind, fields);

    protected override void Set(ShoppingList row, OpField field)
    {
        switch (field.Name)
        {
            case ShoppingListRules.StartsOnField:
                row.StartsOn = OpValues.TryParseDate(field.Value, out DateOnly starts) ? starts : throw Unreadable(field);
                break;
            case ShoppingListRules.EndsOnField:
                row.EndsOn = OpValues.TryParseDate(field.Value, out DateOnly ends) ? ends : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A shopping list has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(ShoppingList row) => ShoppingListRules.CreateFields(row.StartsOn, row.EndsOn);

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}

/// <summary>How an op's fields map onto a <see cref="ShoppingListItem"/> row.</summary>
public sealed class ShoppingListItemEntity : SyncedEntity<ShoppingListItem>
{
    public override string Entity => ShoppingItemRules.Entity;

    public override string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) => ShoppingItemRules.OpProblem(kind, fields);

    protected override void Set(ShoppingListItem row, OpField field)
    {
        switch (field.Name)
        {
            case ShoppingItemRules.ListField:
                row.ListId = OpValues.TryParseId(field.Value, out Guid? list) && list is { } id ? id : throw Unreadable(field);
                break;
            case ShoppingItemRules.IngredientField:
                row.IngredientId = OpValues.TryParseId(field.Value, out Guid? ingredient) ? ingredient : throw Unreadable(field);
                break;
            case ShoppingItemRules.NameField:
                row.Name = field.Value;
                break;
            case ShoppingItemRules.StoreField:
                row.StoreId = OpValues.TryParseId(field.Value, out Guid? store) ? store : throw Unreadable(field);
                break;
            case ShoppingItemRules.PriceCentsField:
                row.PriceCents = field.Value is null ? null : OpValues.TryParseCents(field.Value, out long cents) ? cents : throw Unreadable(field);
                break;
            case ShoppingItemRules.TickField:
                row.Tick = ShoppingTicks.TryParse(field.Value, out ShoppingTick tick) ? tick : throw Unreadable(field);
                break;
            default:
                throw new ArgumentException($"A shopping item has no field '{field.Name}'.", nameof(field));
        }
    }

    protected override IReadOnlyList<OpField> Read(ShoppingListItem row)
    {
        // A tick's fields, or an extra's: never both, as a create would be refused with both.
        List<OpField> fields = [new(ShoppingItemRules.ListField, OpValues.FormatId(row.ListId))];
        if (row.IngredientId is { } ingredient)
            fields.Add(new(ShoppingItemRules.IngredientField, OpValues.FormatId(ingredient)));
        else
        {
            fields.Add(new(ShoppingItemRules.NameField, row.Name));
            fields.Add(new(ShoppingItemRules.StoreField, OpValues.FormatId(row.StoreId)));
            fields.Add(new(ShoppingItemRules.PriceCentsField, row.PriceCents is { } cents ? OpValues.FormatCents(cents) : null));
        }
        fields.Add(new(ShoppingItemRules.TickField, ShoppingTicks.Format(row.Tick)));
        return fields;
    }

    private static ArgumentException Unreadable(OpField field) =>
        new($"'{field.Value}' is not a valid {field.Name}.", nameof(field));
}
