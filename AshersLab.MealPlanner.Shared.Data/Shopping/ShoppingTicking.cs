using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Shopping;

/// <summary>One op a tick writes. The browser writes them all as one (<c>LocalOps.WriteAllAsync</c>): all or none.</summary>
public sealed record ShoppingEdit(SyncedEntity Type, Guid Id, OpKind Kind, IReadOnlyList<OpField> Fields);

/// <summary>
/// What ticking a line does, as ops. <b>A tick first undoes the line's last tick</b> - deletes the stock movements it wrote
/// (<see cref="StockMovement.ShoppingItemId"/>) - then writes its own, so bought → have it → bought never adds a pack
/// twice, and unticking takes the packs back out of the pantry:
/// <list type="table">
/// <item><term>Bought</term><description>a <c>Bought</c> movement of the whole packs (the spare goes into the pantry with
///   them); for a staple, its state back to Have instead - staples are not counted by amount.</description></item>
/// <item><term>Have it</term><description>an <c>Adjusted</c> movement of the shortfall, when the pantry thought there was
///   less than the meals need - it had it wrong, and now has enough. Nothing for a line it already covered. A staple:
///   back to Have.</description></item>
/// <item><term>None</term><description>nothing more: the undo is all. A staple goes back to running low.</description></item>
/// </list>
/// Every movement is dated <paramref name="today"/>, like the pantry's own.
/// </summary>
public static class ShoppingTicking
{
    public static async Task<List<ShoppingEdit>> EditsAsync(DbContext db, ShoppingBook book, ShoppingLine line, ShoppingTick to,
                                                            DateOnly today, Func<Guid> newId, CancellationToken cancellationToken = default)
    {
        if (line.Tick == to)
            return [];

        List<ShoppingEdit> edits = [];
        if (!await db.Set<ShoppingList>().AnyAsync(l => l.Id == book.ListId, cancellationToken))
            edits.Add(new(SyncedEntities.ShoppingList, book.ListId, OpKind.Create, ShoppingListRules.CreateFields(book.StartsOn, book.EndsOn)));

        foreach (Guid written in await db.Set<StockMovement>().AsNoTracking()
                     .Where(m => !m.Deleted && m.ShoppingItemId == line.ItemId).Select(m => m.Id).ToListAsync(cancellationToken))
            edits.Add(new(SyncedEntities.StockMovement, written, OpKind.Delete, []));

        edits.Add(new(SyncedEntities.ShoppingListItem, line.ItemId, OpKind.Create, ShoppingItemRules.TickFields(book.ListId, line.Ingredient.Id, to)));

        Ingredient ingredient = line.Ingredient;
        if (ingredient.IsStaple)
        {
            StapleStock state = to == ShoppingTick.None ? StapleStock.RunningLow : StapleStock.Have;
            if (ingredient.StapleStock != state && !(to == ShoppingTick.None && ingredient.StapleStock == StapleStock.Out))
                edits.Add(new(SyncedEntities.Ingredient, ingredient.Id, OpKind.Update, [new OpField(IngredientRules.StapleStockField, StapleStocks.Format(state))]));
            return edits;
        }

        StockEntry? movement = to switch
        {
            ShoppingTick.Bought when line.BoughtQuantity > 0  => new StockEntry(ingredient.Id, line.BoughtQuantity, today, StockReason.Bought, line.ItemId),
            ShoppingTick.HaveIt when line.HaveItAdjustment > 0 => new StockEntry(ingredient.Id, line.HaveItAdjustment, today, StockReason.Adjusted, line.ItemId),
            _                                                  => null,
        };
        if (movement is not null)
            edits.Add(new(SyncedEntities.StockMovement, newId(), OpKind.Create, movement.Fields()));
        return edits;
    }

    /// <summary>The list's row, for an extra item added to a list nobody has ticked yet; nothing when it exists.</summary>
    public static async Task<List<ShoppingEdit>> ListAsync(DbContext db, DateOnly startsOn, DateOnly endsOn, CancellationToken cancellationToken = default)
    {
        Guid id = ShoppingListRules.ListId(startsOn, endsOn);
        return await db.Set<ShoppingList>().AnyAsync(l => l.Id == id, cancellationToken)
            ? []
            : [new(SyncedEntities.ShoppingList, id, OpKind.Create, ShoppingListRules.CreateFields(startsOn, endsOn))];
    }
}
