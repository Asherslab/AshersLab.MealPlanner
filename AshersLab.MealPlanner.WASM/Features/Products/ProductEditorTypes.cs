using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;

namespace AshersLab.MealPlanner.WASM.Features.Products;

/// <summary>
/// What the product editor answers: the edited product, or a confirmed delete. A new product may come with its first
/// price, so a product and its price are one dialog.
/// </summary>
public sealed record ProductEditorResult(ProductDraft? Saved, bool Delete, long? FirstPriceCents = null);

/// <summary>What the price sheet answers: a new price, a price to delete, or "edit the product instead".</summary>
public sealed record PriceSheetResult(PriceEntry? Entry, Guid? DeleteId, bool OpenEditor)
{
    public static PriceSheetResult Saved(PriceEntry entry) => new(entry, null, false);

    public static PriceSheetResult Deleted(Guid id) => new(null, id, false);

    public static readonly PriceSheetResult EditProduct = new(null, null, true);
}

/// <summary>A choice in the product editor's ingredient picker, with the unit its pack size is read in.</summary>
public sealed record IngredientOption(Guid Id, string Name, BaseUnit Unit);
