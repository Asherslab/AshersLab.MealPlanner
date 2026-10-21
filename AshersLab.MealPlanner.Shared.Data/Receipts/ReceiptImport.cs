using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Shopping;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Pantry;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Receipts;

/// <summary>One op an import writes. The browser writes them all as one (<c>LocalOps.WriteAllAsync</c>): all or none.</summary>
public sealed record ReceiptEdit(SyncedEntity Type, Guid Id, OpKind Kind, IReadOnlyList<OpField> Fields);

/// <summary>A product to make for a line nothing matched: under an ingredient, or none for a household thing.</summary>
public sealed record NewProduct(string Name, Guid? IngredientId, decimal PackQuantity);

/// <summary>
/// What the planner decided for one line in the review. <paramref name="ProductId"/> or <paramref name="New"/> matches it;
/// neither leaves it unmatched (imported, but pricing and stocking nothing); <paramref name="Ignore"/> leaves it out.
/// </summary>
/// <param name="Covers">The shopping-list tick this purchase replaces (<see cref="ReceiptStock"/>), or null.</param>
public sealed record LineDecision(Guid? ProductId, NewProduct? New, bool Ignore, Guid? Covers);

/// <summary>
/// The review's choice for a line as the page holds it - "" not matched, "p:{product}", "n:{ingredient}" a new product of it,
/// "h" a new product that is not food, "x" left out - and what it decides.
/// </summary>
public static class ReviewChoice
{
    public static (Guid? Product, Guid? Ingredient) Target(string choice) =>
        choice.StartsWith("p:") && Guid.TryParse(choice[2..], out Guid p) ? (p, null)
        : choice.StartsWith("n:") && Guid.TryParse(choice[2..], out Guid i) ? (null, i)
        : (null, null);

    public static bool MakesProduct(string choice) => choice.StartsWith("n:") || choice == "h";

    /// <summary>
    /// The decision, or null while a new product lacks a valid name or pack. <paramref name="tick"/> is the shopping-list tick
    /// the purchase would replace; <b>it is replaced only while <paramref name="cover"/> is on</b> - the planner turns it off
    /// for two shops of the same thing.
    /// </summary>
    public static LineDecision? Decide(string choice, string? newName, string? newPack, BaseUnit unit, Guid? tick, bool cover)
    {
        (Guid? product, Guid? ingredient) = Target(choice);
        Guid? covers = cover ? tick : null;
        if (choice == "x")
            return new LineDecision(null, null, true, null);
        if (product is { } p)
            return new LineDecision(p, null, false, covers);
        if (MakesProduct(choice))
        {
            string name = Names.Normalise(newName);
            decimal? pack = PackSize.Parse(newPack, unit, out _);
            return Names.Problem(name, "A product") is null && pack is { } size
                ? new LineDecision(null, new NewProduct(name, ingredient, size), false, covers)
                : null;
        }
        return new LineDecision(null, null, false, null);
    }
}

/// <summary>A receipt line in the review: what it is, what it matched, what it might be, and what the planner decided.</summary>
public sealed record ReviewLine(ParsedLine Line, LineMatch Match, LineDecision Decision);

/// <summary>
/// Everything the review of one receipt needs, from either database: the store's products and aliases, the ingredients, the
/// shopping-list ticks a purchase may replace, and whether this order is in already. Then <see cref="Edits"/> turns the
/// planner's decisions into ops.
/// </summary>
public sealed class ReceiptImport
{
    private ReceiptImport(Guid storeId, ParsedReceipt parsed, Receipt? earlier, Dictionary<Guid, Product> products,
                          Dictionary<string, ProductAlias> aliases, Dictionary<Guid, Ingredient> ingredients, List<TickBought> ticks)
    {
        StoreId     = storeId;
        Parsed      = parsed;
        Earlier     = earlier;
        Products    = products;
        Aliases     = aliases;
        Ingredients = ingredients;
        Ticks       = ticks;
    }

    public Guid StoreId { get; }

    public ParsedReceipt Parsed { get; }

    public Guid ReceiptId => ReceiptRules.ReceiptId(StoreId, Parsed.OrderNumber);

    /// <summary><b>The same order imported before</b>, under any store: a second import is refused, naming this one.</summary>
    public Receipt? Earlier { get; }

    /// <summary>The store's live products.</summary>
    public IReadOnlyDictionary<Guid, Product> Products { get; }

    /// <summary>The store's aliases by <see cref="ProductAliasRules.Key"/>.</summary>
    public IReadOnlyDictionary<string, ProductAlias> Aliases { get; }

    public IReadOnlyDictionary<Guid, Ingredient> Ingredients { get; }

    /// <summary>Shopping-list lines ticked bought whose pack is still in the pantry, not replaced by a receipt yet.</summary>
    public IReadOnlyList<TickBought> Ticks { get; }

    public static async Task<ReceiptImport> LoadAsync(DbContext db, Guid storeId, ParsedReceipt parsed, CancellationToken cancellationToken = default)
    {
        // The same order under any store: a receipt's id is derived from its store too, and an order imported under the
        // wrong one must not let the same purchase in twice.
        Guid id = ReceiptRules.ReceiptId(storeId, parsed.OrderNumber);
        string order = Names.Normalise(parsed.OrderNumber);
        Receipt? earlier = await db.Set<Receipt>().AsNoTracking().Where(r => !r.Deleted && (r.Id == id || r.OrderNumber == order))
            .OrderBy(r => r.ImportedOn).FirstOrDefaultAsync(cancellationToken);
        Dictionary<Guid, Product> products = await db.Set<Product>().AsNoTracking().Where(p => !p.Deleted && p.StoreId == storeId)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        Dictionary<string, ProductAlias> aliases = (await db.Set<ProductAlias>().AsNoTracking().Where(a => !a.Deleted && a.StoreId == storeId)
            .ToListAsync(cancellationToken)).GroupBy(a => ProductAliasRules.Key(a.Text)).ToDictionary(g => g.Key, g => g.First());
        Dictionary<Guid, Ingredient> ingredients = await db.Set<Ingredient>().AsNoTracking().Where(i => !i.Deleted)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        Dictionary<Guid, ShoppingTick> ticks = await db.Set<ShoppingListItem>().AsNoTracking().Where(i => !i.Deleted && i.IngredientId != null)
            .ToDictionaryAsync(i => i.Id, i => i.Tick, cancellationToken);
        List<StockMovement> movements = await db.Set<StockMovement>().AsNoTracking().Where(m => !m.Deleted).ToListAsync(cancellationToken);
        List<TickBought> bought = [.. StockMovement.Counting(movements, ticks)
            .Where(m => m.ShoppingItemId is not null && m.Reason == StockReason.Bought)
            .Select(m => new TickBought(m.ShoppingItemId!.Value, m.IngredientId, m.OccurredOn, m.Quantity))];
        return new ReceiptImport(storeId, parsed, earlier, products, aliases, ingredients, bought);
    }

    /// <summary>The ingredient a product is bought as, if it is one that is live.</summary>
    public Ingredient? IngredientOf(Guid? productId) =>
        productId is { } p && Products.GetValueOrDefault(p)?.IngredientId is { } i ? Ingredients.GetValueOrDefault(i) : null;

    /// <summary>
    /// Every line matched as far as it matches by itself (store code, then alias), with its suggestions, and the tick each
    /// food line's purchase replaces - the defaults the review opens with.
    /// </summary>
    public List<ReviewLine> Review()
    {
        List<MatchProduct> products = [.. Products.Values.Select(p => new MatchProduct(p.Id, p.Name, p.StoreCode, p.IngredientId))];
        Dictionary<string, Guid> aliases = Aliases.ToDictionary(a => a.Key, a => a.Value.ProductId);
        List<MatchIngredient> ingredients = [.. Ingredients.Values.Select(i => new MatchIngredient(i.Id, i.Name))];
        HashSet<Guid> taken = [];
        List<ReviewLine> lines = [];
        foreach (ParsedLine line in Parsed.Lines)
        {
            LineMatch match = ReceiptMatching.Match(line, products, aliases, ingredients);
            Guid? covers = CoverFor(line, match.ProductId, taken);
            if (covers is { } c)
                taken.Add(c);
            lines.Add(new ReviewLine(line, match, new LineDecision(match.ProductId, null, false, covers)));
        }
        return lines;
    }

    /// <summary>The tick a line's purchase would replace, once matched to <paramref name="productId"/>; <paramref name="taken"/> are spoken for.</summary>
    public Guid? CoverFor(ParsedLine line, Guid? productId, IReadOnlySet<Guid> taken, Guid? ingredientId = null) =>
        line.NotSupplied || (ingredientId is { } i ? Ingredients.GetValueOrDefault(i) : IngredientOf(productId)) is not { IsStaple: false } ingredient
            ? null
            : ReceiptStock.Covers(ingredient.Id, Parsed.PurchasedOn, Ticks, taken)?.ItemId;

    /// <summary>What the line will be imported as, for what the review shows and the line's kind.</summary>
    public ReceiptLineKind KindOf(ParsedLine line, LineDecision decision)
    {
        if (decision.Ignore)
            return ReceiptLineKind.Ignored;
        Guid? ingredient = decision.New is { } n ? n.IngredientId : decision.ProductId is { } p ? Products.GetValueOrDefault(p)?.IngredientId : null;
        if (decision.New is not null || decision.ProductId is not null)
            return ingredient is null ? ReceiptLineKind.Household : ReceiptLineKind.Food;
        return line.Gst || HouseholdHint(line.Category) ? ReceiptLineKind.Household : ReceiptLineKind.Food;
    }

    /// <summary>Normalised and cut to <paramref name="max"/> characters: a store's heading is a hint, not worth refusing an import over.</summary>
    public static string Clip(string text, int max)
    {
        string normal = Names.Normalise(text);
        return normal.Length <= max ? normal : normal[..max].TrimEnd();
    }

    private static bool HouseholdHint(string? category) =>
        category is not null && new[] { "household", "toiletries", "personal care", "cleaning", "baby", "pet" }
            .Any(w => category.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The ops that import the receipt as reviewed, every id but a new product's derived (<see cref="ReceiptRules"/>):
    /// <list type="bullet">
    /// <item>the receipt, and a line for each of its lines and each fee that cost something;</item>
    /// <item>a new product for each line the planner made one for;</item>
    /// <item>an alias for every line matched any way but its alias or code - <b>the next receipt then matches by itself</b>;</item>
    /// <item>for each matched line something came home for, a <c>Receipt</c> price for its product, seen on the receipt's date;</item>
    /// <item>for each food line, a <c>Bought</c> movement of what came home, naming the tick it replaces. Staples have no
    ///   movements: their stock is the hand-set state.</item>
    /// </list>
    /// </summary>
    /// <exception cref="InvalidOperationException">The receipt does not add up, or was imported before.</exception>
    public List<ReceiptEdit> Edits(IReadOnlyList<LineDecision> decisions, DateOnly today, Func<Guid> newId)
    {
        if (Parsed.TotalsProblem() is { } problem)
            throw new InvalidOperationException(problem);
        if (Earlier is not null)
            throw new InvalidOperationException("This receipt was imported already.");
        if (decisions.Count != Parsed.Lines.Count)
            throw new ArgumentException("One decision for each line.", nameof(decisions));

        Guid receiptId = ReceiptId;
        List<ReceiptEdit> edits =
        [
            new(SyncedEntities.Receipt, receiptId, OpKind.Create, ReceiptFields.Of(new Receipt
            {
                StoreId = StoreId, OrderNumber = Names.Normalise(Parsed.OrderNumber), PurchasedOn = Parsed.PurchasedOn,
                SubtotalCents = Parsed.SubtotalCents, FeesCents = Parsed.FeesCents, TotalCents = Parsed.TotalCents,
                PaidCents = Parsed.PaidCents, RefundCents = Parsed.RefundCents, ImportedOn = today,
            })),
        ];

        HashSet<Guid> covered = [];
        for (int i = 0; i < Parsed.Lines.Count; i++)
        {
            ParsedLine line = Parsed.Lines[i];
            LineDecision decision = decisions[i];
            Guid lineId = ReceiptRules.LineId(receiptId, line.Position);

            Guid? productId = null;
            decimal pack = 0;
            Guid? ingredientId = null;
            if (!decision.Ignore && decision.New is { } made)
            {
                productId = newId();
                (pack, ingredientId) = (made.PackQuantity, made.IngredientId);
                edits.Add(new(SyncedEntities.Product, productId.Value, OpKind.Create,
                    new ProductDraft(Names.Normalise(made.Name), StoreId, line.StoreCode, made.PackQuantity, made.IngredientId).FieldsChangedFrom(null)));
            }
            else if (!decision.Ignore && decision.ProductId is { } chosen && Products.GetValueOrDefault(chosen) is { } product)
            {
                productId = chosen;
                (pack, ingredientId) = (product.PackQuantity, product.IngredientId);
            }

            edits.Add(new(SyncedEntities.ReceiptLine, lineId, OpKind.Create, ReceiptFields.Of(new ReceiptLine
            {
                ReceiptId = receiptId, Position = line.Position, Description = line.Description, StoreCode = line.StoreCode,
                Category = line.Category is { } c ? Clip(c, Names.MaxLength) : null,
                Quantity = line.Supplied, UnitPriceCents = line.UnitPriceCents, AmountCents = line.AmountCents,
                WeightGrams = line.WeightGrams, PerKgCents = line.PerKgCents, DiscountCents = line.DiscountCents,
                ProductId = productId, Kind = KindOf(line, decision),
            })));
            if (productId is not { } matched)
                continue;

            // Teach the alias, unless the line matched by it (or by the store's code) already.
            string key = ProductAliasRules.Key(line.Description);
            if (Aliases.GetValueOrDefault(key)?.ProductId != matched && line.StoreCode is null)
                edits.Add(new(SyncedEntities.ProductAlias, ProductAliasRules.AliasId(StoreId, line.Description), OpKind.Create,
                    ReceiptFields.Of(new ProductAlias { StoreId = StoreId, Text = line.Description, ProductId = matched })));

            if (line.NotSupplied)
                continue;
            if (line.PackPriceCents is { } price && Money.CentsProblem(price) is null)
                edits.Add(new(SyncedEntities.PriceObservation, ReceiptRules.PriceId(lineId), OpKind.Create,
                    new PriceEntry(matched, price, Parsed.PurchasedOn, PriceSource.Receipt, line.PerKgCents).Fields()));

            if (ingredientId is { } ing && Ingredients.GetValueOrDefault(ing) is { IsStaple: false } ingredient
                && ReceiptStock.Quantity(line, pack, ingredient.BaseUnit) is { } quantity && StockRules.QuantityProblem(StockReason.Bought, quantity) is null)
            {
                Guid? covers = decision.Covers is { } tick && covered.Add(tick) ? tick : null;
                edits.Add(new(SyncedEntities.StockMovement, ReceiptRules.StockId(lineId), OpKind.Create,
                    new StockEntry(ingredient.Id, quantity, Parsed.PurchasedOn, StockReason.Bought, null, covers).Fields()));
            }
        }

        int position = Parsed.Lines.Count == 0 ? 0 : Parsed.Lines.Max(l => l.Position) + 1;
        foreach (ParsedFee fee in Parsed.Fees.Where(f => f.AmountCents != 0))
        {
            edits.Add(new(SyncedEntities.ReceiptLine, ReceiptRules.LineId(receiptId, position), OpKind.Create, ReceiptFields.Of(new ReceiptLine
            {
                ReceiptId = receiptId, Position = position, Description = Names.Normalise(fee.Name), Quantity = 1,
                AmountCents = fee.AmountCents, Kind = ReceiptLineKind.Fee,
            })));
            position++;
        }
        return edits;
    }
}
