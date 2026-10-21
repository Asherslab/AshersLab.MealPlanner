using AshersLab.MealPlanner.Shared.Data.Sync;

namespace AshersLab.MealPlanner.Shared.Data.Products;

/// <summary>
/// A thing a store actually sells - "Costco beef mince 8x250g" - and the ingredient it is bought as. The same row in
/// the server's database and in every browser's; only ever written by applying an op.
/// </summary>
public class Product : ISyncedRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Not a foreign key (docs/modules/catalogue/catalogue.md): a store gone elsewhere is shown as removed.</summary>
    public Guid StoreId { get; set; }

    /// <summary>Woolworths stockcode or Costco item number; null when not known.</summary>
    public string? StoreCode { get; set; }

    /// <summary>
    /// How much is in one pack, in the ingredient's base unit (2000 for a 2 kg pack of an ingredient counted in
    /// grams). Read in whatever unit the ingredient has now: changing the ingredient's unit converts nothing.
    /// </summary>
    public decimal PackQuantity { get; set; }

    /// <summary>Null for a household item. Not a foreign key, as <see cref="StoreId"/>.</summary>
    public Guid? IngredientId { get; set; }

    public bool Deleted { get; set; }
}
