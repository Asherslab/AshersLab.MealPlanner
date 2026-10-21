using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Ingredients;

/// <summary>
/// An ingredient as the op log sees it: its entity name, its fields, and what a valid value is. The page, the
/// browser's op writer and the server's push all check the same rules, so an op the browser queued is one
/// the server accepts.
/// </summary>
public static class IngredientRules
{
    /// <summary>The op log's <c>Entity</c> for ingredients. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Ingredient";

    public const string NameField = "Name";

    /// <summary>A <see cref="Ingredients.BaseUnit"/> by name.</summary>
    public const string BaseUnitField = "BaseUnit";

    /// <summary>A category's id (<see cref="OpValues.FormatId"/>), or null for none.</summary>
    public const string CategoryField = "CategoryId";

    /// <summary>A store's id, or null for none: where it goes on the shopping list.</summary>
    public const string UsualStoreField = "UsualStoreId";

    /// <summary>"true" or "false". Staples are used in small amounts and left off the shopping list.</summary>
    public const string IsStapleField = "IsStaple";

    /// <summary>
    /// "true" when the unit is the spreadsheet import's guess and nobody has confirmed it; saving the ingredient's
    /// editor sends "false". Every quantity of it is counted in the unit, so a wrong guess is wrong everywhere.
    /// </summary>
    public const string UnitGuessedField = "UnitGuessed";

    /// <summary>
    /// A staple's <see cref="Ingredients.StapleStock"/> by name - have, running low or out. Set from the pantry, one
    /// field to an op, so a tap on one phone never undoes another field edited elsewhere.
    /// </summary>
    public const string StapleStockField = "StapleStock";

    public const int MaxNameLength = Names.MaxLength;

    private const string Noun = "An ingredient";

    /// <summary>Every field an ingredient op may carry. Anything else is refused.</summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        NameField, BaseUnitField, CategoryField, UsualStoreField, IsStapleField, UnitGuessedField, StapleStockField,
    };

    /// <summary>
    /// A new ingredient needs a name and a unit - the unit because every quantity of it, in every recipe and pack,
    /// is counted in it. Category, store and staple may come later.
    /// </summary>
    private static readonly string[] RequiredOnCreate = [NameField, BaseUnitField];

    /// <summary>Trims and collapses inner runs of whitespace: " beef  mince " is "beef mince".</summary>
    public static string NormaliseName(string? name) => Names.Normalise(name);

    /// <summary>Null when <paramref name="name"/> (already normalised) is acceptable; otherwise why not.</summary>
    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    /// <summary>
    /// Null when an op's fields are acceptable for <paramref name="kind"/>; otherwise why not. A create must
    /// name the ingredient and give its unit; an update may carry only known fields, each valid. Whether a
    /// referenced category or store exists is not checked - it may not have arrived yet, or been deleted elsewhere.
    /// </summary>
    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField       => Names.FieldProblem(field.Value, Noun),
            BaseUnitField   => BaseUnits.TryParse(field.Value, out _) ? null : "The unit is not grams, millilitres or each.",
            CategoryField   => OpValues.TryParseId(field.Value, out _) ? null : "The category is not an id.",
            UsualStoreField => OpValues.TryParseId(field.Value, out _) ? null : "The store is not an id.",
            IsStapleField   => OpValues.TryParseBool(field.Value, out _) ? null : "Staple is not true or false.",
            UnitGuessedField => OpValues.TryParseBool(field.Value, out _) ? null : "Unit guessed is not true or false.",
            StapleStockField => StapleStocks.TryParse(field.Value, out _) ? null : "The staple's stock is not have, running low or out.",
            _               => null,
        });
}
