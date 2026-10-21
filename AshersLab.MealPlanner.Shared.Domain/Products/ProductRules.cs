using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Products;

/// <summary>
/// A product - a thing a store actually sells, "Costco beef mince 8x250g" - as the op log sees it. Many products can
/// stand for one ingredient (one per store, sometimes more); a product with no ingredient is a household item.
/// </summary>
public static class ProductRules
{
    /// <summary>The op log's <c>Entity</c> for products. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Product";

    public const string NameField = "Name";

    /// <summary>The store's id. Required: a product is always something one store sells.</summary>
    public const string StoreField = "StoreId";

    /// <summary>The Woolworths stockcode or Costco item number, or null when not known. What a receipt matches on first.</summary>
    public const string StoreCodeField = "StoreCode";

    /// <summary>How much is in one pack, in the ingredient's base unit (<see cref="OpValues.FormatDecimal"/>).</summary>
    public const string PackQuantityField = "PackQuantity";

    /// <summary>The ingredient it is bought as, or null for none (household items).</summary>
    public const string IngredientField = "IngredientId";

    public const int MaxStoreCodeLength = 50;

    public const string Noun = "A product";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        NameField, StoreField, StoreCodeField, PackQuantityField, IngredientField,
    };

    /// <summary>
    /// A new product names itself, its store and its pack size. The pack size is what turns a pack's price into a
    /// price per gram, so a product without one could never price anything.
    /// </summary>
    private static readonly string[] RequiredOnCreate = [NameField, StoreField, PackQuantityField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    /// <summary>A store code is trimmed, single-spaced and short; null means none, never an empty string.</summary>
    public static string? NormaliseStoreCode(string? code) => Names.Normalise(code) is { Length: > 0 } normal ? normal : null;

    public static string? StoreCodeProblem(string? code) =>
        code is null                             ? null
        : NormaliseStoreCode(code) != code       ? "The store code is not normalised."
        : code.Length > MaxStoreCodeLength       ? $"Keep the store code to {MaxStoreCodeLength} characters."
        : null;

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. Whether the store or
    /// ingredient exists is not checked - it may not have arrived yet, or been deleted elsewhere.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField         => Names.FieldProblem(field.Value, Noun),
            StoreField        => OpValues.TryParseId(field.Value, out Guid? store) && store is not null ? null : "A product needs a store.",
            StoreCodeField    => StoreCodeProblem(field.Value),
            PackQuantityField => OpValues.TryParseDecimal(field.Value, out decimal quantity)
                ? PackSize.Problem(quantity)
                : "The pack quantity is not a number.",
            IngredientField   => OpValues.TryParseId(field.Value, out _) ? null : "The ingredient is not an id.",
            _                 => null,
        });
}
