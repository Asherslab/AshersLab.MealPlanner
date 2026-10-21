using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Stores;

/// <summary>
/// A store - Woolworths, Costco, the butcher - as the op log sees it. Only a name: what a store sells and
/// for how much is the product slice's.
/// </summary>
public static class StoreRules
{
    /// <summary>The op log's <c>Entity</c> for stores. On the wire and in both databases - never rename.</summary>
    public const string Entity = "Store";

    public const string NameField = "Name";

    public const string Noun = "A store";

    /// <summary>
    /// The seeded Woolworths: the same id in every database (the server's <c>CatalogueSeed</c>). A receipt reader names
    /// its store by this id, never by a name the planner can change - a receipt's id is derived from its store.
    /// </summary>
    public static readonly Guid Woolworths = new("00000000-5eed-7000-8000-000000000101");

    /// <summary>The seeded Costco, as <see cref="Woolworths"/>.</summary>
    public static readonly Guid Costco = new("00000000-5eed-7000-8000-000000000102");

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { NameField };

    private static readonly string[] RequiredOnCreate = [NameField];

    public static string? NameProblem(string name) => Names.Problem(name, Noun);

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields) =>
        OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
        {
            NameField => Names.FieldProblem(field.Value, Noun),
            _         => null,
        });
}
