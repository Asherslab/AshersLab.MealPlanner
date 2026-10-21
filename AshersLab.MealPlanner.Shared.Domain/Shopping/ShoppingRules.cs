using System.Security.Cryptography;
using System.Text;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Shopping;

/// <summary>
/// What a shopping-list line has been ticked as. On the wire and in both databases by <b>name</b> - never rename a member.
/// </summary>
public enum ShoppingTick
{
    /// <summary>Not ticked - or unticked again.</summary>
    None,

    /// <summary>"We already have it": not bought, and the pantry is told so when it thought there was not enough.</summary>
    HaveIt,

    /// <summary>In the trolley: the whole packs go into the pantry.</summary>
    Bought,
}

public static class ShoppingTicks
{
    public static string Format(ShoppingTick tick) => tick.ToString();

    /// <summary>Reads exactly a member's name, case and all - nothing else, numbers included.</summary>
    public static bool TryParse(string? value, out ShoppingTick tick)
    {
        foreach (ShoppingTick candidate in Enum.GetValues<ShoppingTick>())
        {
            if (candidate.ToString() == value)
            {
                tick = candidate;
                return true;
            }
        }
        tick = default;
        return false;
    }
}

/// <summary>
/// A shopping list - a date range - as the op log sees it. Its lines are computed from the calendar and the pantry, never
/// stored; only the ticks and the extra items are (<see cref="ShoppingItemRules"/>).
/// </summary>
/// <remarks>
/// <b>Its id is derived from its dates</b> (<see cref="ListId"/>), so two phones making "this week's list" offline make the
/// same row, and its create carries only the dates, which never change. A different range is a different list.
/// </remarks>
public static class ShoppingListRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "ShoppingList";

    /// <summary>The first day whose meals it shops for (<see cref="OpValues.FormatDate"/>). Never changed.</summary>
    public const string StartsOnField = "StartsOn";

    /// <summary>The last day whose meals it shops for. Never changed.</summary>
    public const string EndsOnField = "EndsOn";

    /// <summary>Five weeks: longer is a typo, and no pack keeps that long anyway.</summary>
    public const int MaxDays = 35;

    public const string Noun = "A shopping list";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal) { StartsOnField, EndsOnField };

    private static readonly string[] RequiredOnCreate = [StartsOnField, EndsOnField];

    /// <summary>Null when the range is a list's; otherwise why not.</summary>
    public static string? RangeProblem(DateOnly startsOn, DateOnly endsOn) =>
        endsOn < startsOn ? "The last day is before the first."
        : endsOn.DayNumber - startsOn.DayNumber + 1 > MaxDays ? $"Keep a list to {MaxDays} days."
        : null;

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields)
    {
        if (OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => OpValues.TryParseDate(field.Value, out _) ? null : "The date is not a date.") is { } problem)
            return problem;
        return fields.FirstOrDefault(f => f.Name == StartsOnField) is { } s && OpValues.TryParseDate(s.Value, out DateOnly starts)
            && fields.FirstOrDefault(f => f.Name == EndsOnField) is { } e && OpValues.TryParseDate(e.Value, out DateOnly ends)
                ? RangeProblem(starts, ends)
                : null;
    }

    /// <summary>The list for a range: a version-8 GUID from the two dates, the same on every device.</summary>
    public static Guid ListId(DateOnly startsOn, DateOnly endsOn) =>
        Derived($"shopping|{OpValues.FormatDate(startsOn)}|{OpValues.FormatDate(endsOn)}");

    public static IReadOnlyList<OpField> CreateFields(DateOnly startsOn, DateOnly endsOn) =>
    [
        new(StartsOnField, OpValues.FormatDate(startsOn)),
        new(EndsOnField, OpValues.FormatDate(endsOn)),
    ];

    internal static Guid Derived(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}

/// <summary>
/// One stored thing on a shopping list, as the op log sees it: either <b>a tick against a computed line</b> (it names an
/// ingredient, and its id is derived from the list and the ingredient - <see cref="TickId"/> - so two phones ticking the
/// same line tick one row, and the later tick wins), or <b>an extra item</b> typed by hand ("bin bags"), with a name, and
/// optionally a store and a price.
/// </summary>
/// <remarks>
/// A tick is never deleted - unticking sets <see cref="ShoppingTick.None"/> - so its derived id is never a tombstone that
/// would ignore the next tick. An extra item is deleted like anything else.
/// </remarks>
public static class ShoppingItemRules
{
    /// <summary>The op log's <c>Entity</c>. On the wire and in both databases - never rename.</summary>
    public const string Entity = "ShoppingListItem";

    /// <summary>The list. Required on create; never changed.</summary>
    public const string ListField = "ListId";

    /// <summary>A tick's ingredient - the line it ticks. Never changed; absent for an extra item.</summary>
    public const string IngredientField = "IngredientId";

    /// <summary>An extra item's name; absent for a tick.</summary>
    public const string NameField = "Name";

    /// <summary>An extra item's store, or null for "anywhere".</summary>
    public const string StoreField = "StoreId";

    /// <summary>An extra item's price in whole cents, or null when nobody said.</summary>
    public const string PriceCentsField = "PriceCents";

    /// <summary>A <see cref="ShoppingTick"/> by name. A new item is <see cref="ShoppingTick.None"/>.</summary>
    public const string TickField = "Tick";

    public const string Noun = "A shopping item";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        ListField, IngredientField, NameField, StoreField, PriceCentsField, TickField,
    };

    private static readonly string[] RequiredOnCreate = [ListField];

    /// <summary>The tick of an ingredient's line on a list: a version-8 GUID from the two, the same on every device.</summary>
    public static Guid TickId(Guid listId, Guid ingredientId) => ShoppingListRules.Derived($"shopping-tick|{listId:N}|{ingredientId:N}");

    public static string? OpProblem(OpKind kind, IReadOnlyList<OpField> fields)
    {
        if (OpRules.Problem(kind, fields, Noun, Fields, RequiredOnCreate, field => field.Name switch
            {
                ListField       => OpValues.TryParseId(field.Value, out Guid? list) && list is not null ? null : "A shopping item needs a list.",
                IngredientField => OpValues.TryParseId(field.Value, out Guid? ingredient) && ingredient is not null ? null : "The ingredient is not an id.",
                NameField       => Names.FieldProblem(field.Value, "An extra item"),
                StoreField      => OpValues.TryParseId(field.Value, out _) ? null : "The store is not an id.",
                PriceCentsField => field.Value is null ? null
                    : OpValues.TryParseCents(field.Value, out long cents) ? Money.CentsProblem(cents) : "The price is not whole cents.",
                TickField       => ShoppingTicks.TryParse(field.Value, out _) ? null : "The tick is not none, have it or bought.",
                _               => null,
            }) is { } problem)
            return problem;

        // A create is a tick (an ingredient) or an extra (a name), never both and never neither.
        if (kind == OpKind.Create)
        {
            bool ingredient = fields.Any(f => f.Name == IngredientField);
            bool name       = fields.Any(f => f.Name == NameField);
            if (ingredient == name)
                return ingredient ? "A shopping item is a tick or an extra, not both." : "A new shopping item needs an ingredient or a name.";
        }
        return null;
    }

    /// <summary>A tick, as the list writes it: the list and ingredient (which never change) and the tick.</summary>
    public static IReadOnlyList<OpField> TickFields(Guid listId, Guid ingredientId, ShoppingTick tick) =>
    [
        new(ListField, OpValues.FormatId(listId)),
        new(IngredientField, OpValues.FormatId(ingredientId)),
        new(TickField, ShoppingTicks.Format(tick)),
    ];
}

/// <summary>An extra item as the planner types it: "bin bags", maybe at Costco, maybe $12.99.</summary>
public sealed record ExtraItemDraft(string Name, Guid? StoreId, long? PriceCents)
{
    /// <summary>Null when it can be saved; otherwise why not.</summary>
    public string? Problem() =>
        Names.Problem(Names.Normalise(Name), "An extra item") ?? (PriceCents is { } cents ? Money.CentsProblem(cents) : null);

    /// <summary>The fields to send: every one for a new item; for an edit, only those that changed from <paramref name="was"/>.</summary>
    public IReadOnlyList<OpField> FieldsChangedFrom(Guid listId, ExtraItemDraft? was)
    {
        string name = Names.Normalise(Name);
        List<OpField> fields = [];
        if (was is null)
            fields.Add(new(ShoppingItemRules.ListField, OpValues.FormatId(listId)));
        if (was is null || Names.Normalise(was.Name) != name)
            fields.Add(new(ShoppingItemRules.NameField, name));
        if (was is null ? StoreId is not null : was.StoreId != StoreId)
            fields.Add(new(ShoppingItemRules.StoreField, OpValues.FormatId(StoreId)));
        if (was is null ? PriceCents is not null : was.PriceCents != PriceCents)
            fields.Add(new(ShoppingItemRules.PriceCentsField, PriceCents is { } cents ? OpValues.FormatCents(cents) : null));
        return fields;
    }
}
