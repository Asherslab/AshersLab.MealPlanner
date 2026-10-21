using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

public sealed record KnownIngredient(Guid Id, string Name, BaseUnit Unit, bool Deleted, bool UnitGuessed = false, Guid? UsualStoreId = null);

public sealed record KnownOrdered(Guid Id, string Name, decimal Position, bool Deleted);

public sealed record KnownNamed(Guid Id, string Name, bool Deleted);

public sealed record KnownProduct(Guid Id, string Name, Guid StoreId, Guid? IngredientId, decimal PackQuantity, bool Deleted);

public sealed record KnownPrice(Guid Id, Guid ProductId, long PriceCents, DateOnly ObservedOn, bool Deleted, PriceSource Source = PriceSource.Manual);

/// <summary>What this browser's database already holds, tombstones included, free of the database rows.</summary>
public sealed record ImportKnown(
    IReadOnlyList<KnownIngredient> Ingredients,
    IReadOnlyList<KnownOrdered> Categories,
    IReadOnlyList<KnownOrdered> MealGroups,
    IReadOnlyList<KnownNamed> Meals,
    IReadOnlyList<KnownNamed> Stores,
    IReadOnlyList<KnownProduct> Products,
    IReadOnlyList<KnownPrice> Prices)
{
    /// <summary>Every word the sheet may use for a store: the live stores' names, and the aliases of those.</summary>
    public IReadOnlyCollection<string> StoreWords =>
    [
        .. Stores.Where(s => !s.Deleted).Select(s => s.Name),
        .. SheetText.StoreAliases.Where(a => Stores.Any(s => !s.Deleted && Names.Same(s.Name, a.Value))).Select(a => a.Key),
    ];

    /// <summary>The live store a word from the sheet names, or null.</summary>
    public Guid? StoreFor(string? word)
    {
        if (word is null)
            return null;
        string name = SheetText.StoreAliases.GetValueOrDefault(word) ?? word;
        return Stores.Where(s => !s.Deleted && Names.Same(s.Name, name)).OrderBy(s => s.Id).Select(s => (Guid?)s.Id).FirstOrDefault();
    }
}

/// <summary>
/// The planner's answers to the review. A key that is absent takes the review's suggestion; one that is present wins,
/// even when its value is null ("make a new one", "single serve", "no store").
/// </summary>
public sealed class ImportChoices
{
    /// <summary>A shopping-category heading: the existing category it fills, or null for a new one.</summary>
    public Dictionary<string, Guid?> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A meal-ideas heading: the existing meal group, or null for a new one.</summary>
    public Dictionary<string, Guid?> MealGroups { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A name the sheet uses that is not exactly an ingredient here (by <see cref="ImportNames.Key"/>) - a meal idea's
    /// ingredient, or a shopping-list name on a later import: the key of the ingredient it is, or null for a new one.
    /// </summary>
    public Dictionary<string, string?> Ingredients { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// A new ingredient's unit, chosen in the review - no longer a guess. For an ingredient already here whose unit is
    /// still the import's own guess, a different unit chosen here changes it (<see cref="UnitQuestion"/>).
    /// </summary>
    public Dictionary<string, BaseUnit> Units { get; } = new(StringComparer.Ordinal);

    /// <summary>A price (<see cref="PriceCell.Id"/>): the store it is at, or null to leave it out.</summary>
    public Dictionary<string, Guid?> PriceStores { get; } = new(StringComparer.Ordinal);

    /// <summary>A price: what it is for, as typed ("24", "2kg"), or null to leave it out.</summary>
    public Dictionary<string, string?> PricePacks { get; } = new(StringComparer.Ordinal);

    /// <summary>A meal (by <see cref="SheetImport.MealKey"/>): the serves a batch makes, or null for a single serve.</summary>
    public Dictionary<string, int?> Serves { get; } = new(StringComparer.Ordinal);

    /// <summary>Where the non-food items are bought. Null: those with no store of their own are left out.</summary>
    public Guid? HouseholdStore { get; set; }

    /// <summary>
    /// Where a price that names no store was seen, when its ingredient has no usual store either - one answer for the
    /// whole sheet, since the planner's sheet names none. Null: those prices are left out.
    /// </summary>
    public Guid? PriceStore { get; set; }
}

/// <summary>
/// An ingredient already here whose unit is still the import's guess, and the sheet's prices say otherwise: "Muffins"
/// guessed as grams, priced "$2ea". Changing it is the planner's call; until then those prices are left out.
/// </summary>
public sealed record UnitQuestion(string Key, string Name, BaseUnit Unit, BaseUnit PriceSays, string Why);

/// <summary>A heading that is not obviously one the app has: "Snacks/sweets" for the group "Snack".</summary>
public sealed record HeadingQuestion(string Heading, IReadOnlyList<KnownOrdered> Options, Guid? Suggested);

/// <summary>
/// A name that is not exactly an ingredient the sheet or the app has, but resembles one: "cheese" in a meal, or a
/// shopping-list name an earlier import knew by another ("Sweet chili sauce"). <paramref name="Meals"/> is empty for the
/// latter, and <paramref name="Place"/> says where it is.
/// </summary>
public sealed record IngredientQuestion(
    string Key, string Name, IReadOnlyList<string> Meals, IReadOnlyList<(string Key, string Name)> Options, string? Suggested,
    SheetPlace? Place = null);

/// <summary>A meal whose serves the sheet does not say for sure.</summary>
public sealed record ServesQuestion(string Key, string Meal, int? Suggested, string Why);

/// <summary>One price, and what is known of it - the review asks for the store and pack when the sheet does not say.</summary>
/// <paramref name="Note"/> is what the sheet said it is for when that is not a pack ("for 10 servings").
public sealed record PriceQuestion(string Id, SheetPlace Place, string Item, bool Household, long Cents, string? Pack, Guid? Store, string? Note = null);

public sealed record ImportReview(
    IReadOnlyList<HeadingQuestion> Categories,
    IReadOnlyList<HeadingQuestion> MealGroups,
    IReadOnlyList<IngredientQuestion> Ingredients,
    IReadOnlyList<ServesQuestion> Serves,
    IReadOnlyList<PriceQuestion> Prices,
    IReadOnlyList<string> HouseholdItems,
    IReadOnlyList<UnreadCell> Unread);

/// <summary>One op the import writes, in order. The page writes them all in one transaction.</summary>
public sealed record ImportEdit(string Entity, Guid Id, OpKind Kind, IReadOnlyList<OpField> Fields);

public sealed record NewIngredientLine(string Key, string Name, BaseUnit Unit, bool Guessed, string? Category);

/// <summary>What one run would write, and what it leaves out and why.</summary>
/// <remarks>
/// <paramref name="Units"/> depends on the answers (which ingredient a name is), so it comes with the plan, not the
/// review. <paramref name="ChangedUnits"/> counts the guessed units the answers changed.
/// </remarks>
public sealed record ImportPlan(
    IReadOnlyList<ImportEdit> Edits,
    IReadOnlyList<NewIngredientLine> NewIngredients,
    int NewCategories,
    int NewMealGroups,
    int NewMeals,
    int NewMealIngredients,
    int NewProducts,
    int NewPrices,
    int AlreadyHere,
    IReadOnlyList<string> LeftOut,
    IReadOnlyList<UnitQuestion> Units,
    int ChangedUnits,
    IReadOnlySet<string> PricesAlreadyHere)
{
    public bool NothingToDo => Edits.Count == 0;
}

/// <summary>
/// Turns the planner's sheet into drafts in the app: shopping categories, ingredients (each unit a marked guess),
/// meal groups, meals with their serves and their ingredients with no quantities, simple meals, household products,
/// and every price the sheet holds as a typed-in price. Pure: what it writes depends only on the sheet, what the
/// database already holds, the planner's answers and the day.
/// </summary>
/// <remarks>
/// <b>Running it twice writes nothing the second time.</b> Everything is matched against what is already here by name
/// - an ingredient by <see cref="ImportNames.Key"/> ("egg" is "Eggs"), the rest by <see cref="Names.Same"/> - and what
/// matches is left alone, never updated: the planner may have fixed it since. Something deleted here - an ingredient, a
/// meal, a product, a price - is not brought back. A product is the same one when its store, its ingredient (or, for a
/// household item, its name) and its pack size match; its prices are written (as <see cref="PriceSource.Sheet"/>) only
/// when the sheet says something it did not say last time, whatever receipts or typed prices came since. So a later
/// download of the sheet, with more prices filled in, adds exactly those prices.
/// </remarks>
public sealed class SheetImport
{
    private readonly ParsedSheet _sheet;
    private readonly ImportKnown _known;

    private readonly Dictionary<string, KnownIngredient> _live = new(StringComparer.Ordinal);
    private readonly HashSet<string>                     _gone = new(StringComparer.Ordinal);
    private readonly List<SheetIngredient>               _sheetIngredients = [];
    private readonly List<(string Name, IReadOnlyList<PriceCell> Prices)> _household = [];

    private sealed record SheetIngredient(string Key, string Name, string? Category, string? UsualStore, List<PriceCell> Prices, SheetPlace Place);

    public SheetImport(ParsedSheet sheet, ImportKnown known)
    {
        _sheet = sheet;
        _known = known;

        foreach (KnownIngredient ingredient in known.Ingredients.OrderBy(i => i.Id))
        {
            string key = ImportNames.Key(ingredient.Name);
            if (!ingredient.Deleted)
                _live.TryAdd(key, ingredient);
        }
        foreach (KnownIngredient ingredient in known.Ingredients.Where(i => i.Deleted))
            if (!_live.ContainsKey(ImportNames.Key(ingredient.Name)))
                _gone.Add(ImportNames.Key(ingredient.Name));

        // The shopping categories first, so a name in both lists keeps its category; then the flat list. Same key, one
        // ingredient: the first spelling wins, every price is kept.
        foreach (CostItemCell item in sheet.Costs)
        {
            if (item.Household)
            {
                int at = _household.FindIndex(h => Names.Same(h.Name, item.Name));
                if (at < 0)
                    _household.Add((item.Name, item.Prices));
                else
                    _household[at] = (_household[at].Name, [.. _household[at].Prices, .. item.Prices]);
                continue;
            }
            AddSheetIngredient(item.Name, item.Header, item.UsualStore, item.Prices, item.Place);
        }
        foreach (FlatItemCell item in sheet.Flat)
            AddSheetIngredient(item.Name, null, null, item.Prices, item.Place);
    }

    private void AddSheetIngredient(string name, string? category, string? usualStore, IReadOnlyList<PriceCell> prices, SheetPlace place)
    {
        string key = ImportNames.Key(name);
        if (key.Length == 0)
            return;
        SheetIngredient? known = _sheetIngredients.FirstOrDefault(i => i.Key == key);
        if (known is null)
            _sheetIngredients.Add(new SheetIngredient(key, name, category, usualStore, [.. prices], place));
        else
        {
            known.Prices.AddRange(prices);
            if (known.Category is null && category is not null)
                _sheetIngredients[_sheetIngredients.IndexOf(known)] = known with { Category = category, UsualStore = known.UsualStore ?? usualStore };
        }
    }

    /// <summary>The key a meal's answers are kept under: its name, normalised, in lower case.</summary>
    public static string MealKey(string name) => Names.Normalise(name).ToLowerInvariant();

    // ---------------------------------------------------------------------------------------------------------------
    // The review: what to ask, and what to suggest.

    public ImportReview Review()
    {
        List<IngredientQuestion> ingredients = [];

        // A shopping-list name not exactly an ingredient here but like one: on a later import, the planner may have
        // written "Sweet chili sauce" where the first import made "Sweet chilli sauce". Only the app's ingredients are
        // offered - two names on the sheet are two ingredients unless their keys are equal.
        foreach (SheetIngredient item in _sheetIngredients.Where(i => !_live.ContainsKey(i.Key) && !_gone.Contains(i.Key) && KnownByProduct(i) is null))
        {
            List<NameCandidate<(string Key, string Name)>> candidates =
                ImportNames.Candidates(item.Name, _live.Select(l => (l.Key, l.Value.Name)), p => p.Name);
            if (candidates.Count == 0)
                continue;
            ingredients.Add(new IngredientQuestion(item.Key, item.Name, [], [.. candidates.Select(c => c.Item)],
                candidates.FirstOrDefault(c => c.Strength >= MatchStrength.Likely)?.Item.Key, item.Place));
        }

        foreach (IGrouping<string, (string Key, string Name, string Meal)> group in MealRefs().GroupBy(r => r.Key))
        {
            if (IsKnownIngredient(group.Key))
                continue;
            (string key, string name, _) = group.First();
            List<NameCandidate<(string Key, string Name)>> candidates = ImportNames.Candidates(name, IngredientPool(), p => p.Name);
            // Nothing like it anywhere: a new ingredient, listed with the others to check - nothing to ask.
            if (candidates.Count == 0)
                continue;
            string? suggested = candidates.FirstOrDefault(c => c.Strength >= MatchStrength.Likely)?.Item.Key;
            ingredients.Add(new IngredientQuestion(key, ImportNames.Tidy(name), [.. group.Select(r => r.Meal).Distinct()],
                [.. candidates.Select(c => c.Item)], suggested));
        }

        List<ServesQuestion> serves = [];
        foreach (MealIdeaCell idea in NewMealIdeas())
        {
            if (idea.Idea.ServesUnsure)
                serves.Add(new ServesQuestion(MealKey(idea.Idea.Name), idea.Idea.Name, idea.Idea.Serves, $"The sheet says \"{idea.Idea.Serves}?x\"."));
            else if (idea.Idea.Ingredients is not null && idea.Idea.Serves is null)
                serves.Add(new ServesQuestion(MealKey(idea.Idea.Name), idea.Idea.Name, null, "The sheet does not say how many serves it makes."));
        }

        List<PriceQuestion> prices = [];
        foreach (SheetIngredient item in _sheetIngredients.Where(i => !_gone.Contains(i.Key)))
            foreach (PriceCell price in item.Prices)
                prices.Add(new PriceQuestion(price.Id, price.Place, item.Name, false, price.Price.Cents, price.Price.Pack,
                    _known.StoreFor(price.Price.Store), price.Price.Note));
        foreach ((string name, IReadOnlyList<PriceCell> cells) in _household)
            foreach (PriceCell price in cells)
                prices.Add(new PriceQuestion(price.Id, price.Place, name, true, price.Price.Cents, price.Price.Pack,
                    _known.StoreFor(price.Price.Store), price.Price.Note));

        return new ImportReview(
            HeadingQuestions(_sheet.CostHeaders.Where(h => !h.Household).Select(h => h.Header), _known.Categories),
            HeadingQuestions(_sheet.MealHeaders.Where(h => _sheet.MealIdeas.Any(m => Names.Same(m.Header, h))), _known.MealGroups),
            ingredients,
            serves,
            prices,
            [.. _household.Select(h => h.Name)],
            _sheet.Unread);
    }

    /// <summary>Only the headings worth asking about: one exactly the same as a category or group is not.</summary>
    private static List<HeadingQuestion> HeadingQuestions(IEnumerable<string> headings, IReadOnlyList<KnownOrdered> known)
    {
        List<KnownOrdered> live = [.. known.Where(k => !k.Deleted).OrderBy(k => k.Position).ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase)];
        List<HeadingQuestion> questions = [];
        foreach (string heading in headings)
        {
            (Guid? suggested, bool same) = SuggestHeading(heading, known);
            if (!same)
                questions.Add(new HeadingQuestion(heading, live, suggested));
        }
        return questions;
    }

    /// <summary>The live category or group a heading most likely is, and whether it is exactly that; null for a new one.</summary>
    private static (Guid? Id, bool Same) SuggestHeading(string heading, IReadOnlyList<KnownOrdered> known)
    {
        NameCandidate<KnownOrdered>? best = ImportNames.Candidates(heading, known.Where(k => !k.Deleted), k => k.Name).FirstOrDefault();
        if (best is null)
            return (null, known.Any(k => k.Deleted && Names.Same(k.Name, heading)));
        return (best.Item.Id, best.Strength == MatchStrength.Same);
    }

    /// <summary>
    /// The live ingredient a shopping-list name was answered as on an earlier import, by the product that import made
    /// for its price - a product keeps the sheet's own name ("Natural yoghurt") under the ingredient it was answered as
    /// ("Greek yoghurt"). So the planner answers once, not on every download. Null when there is none.
    /// </summary>
    private string? KnownByProduct(SheetIngredient item) =>
        _known.Products
            .Where(p => !p.Deleted && p.IngredientId is not null && Names.Same(p.Name, item.Name))
            .Select(p => _live.FirstOrDefault(l => l.Value.Id == p.IngredientId).Key)
            .FirstOrDefault(key => key is not null);

    private bool IsKnownIngredient(string key) => _live.ContainsKey(key) || _gone.Contains(key) || _sheetIngredients.Any(i => i.Key == key);

    /// <summary>Every ingredient a meal could be made of: the app's, and the sheet's not yet in the app. By key.</summary>
    private List<(string Key, string Name)> IngredientPool() =>
    [
        .. _live.Select(l => (l.Key, l.Value.Name)),
        .. _sheetIngredients.Where(i => !_live.ContainsKey(i.Key) && !_gone.Contains(i.Key)).Select(i => (i.Key, i.Name)),
    ];

    private IEnumerable<(string Key, string Name, string Meal)> MealRefs() =>
        NewMealIdeas().SelectMany(m => (m.Idea.Ingredients ?? []).Select(r => (ImportNames.Key(r.Name), r.Name, m.Idea.Name)))
            .Where(r => r.Item1.Length > 0);

    /// <summary>The meal ideas that would be new: the first of each name, and none already here or deleted here.</summary>
    private IEnumerable<MealIdeaCell> NewMealIdeas()
    {
        List<string> seen = [];
        foreach (MealIdeaCell idea in _sheet.MealIdeas)
        {
            if (seen.Any(s => Names.Same(s, idea.Idea.Name)) || _known.Meals.Any(m => Names.Same(m.Name, idea.Idea.Name)))
                continue;
            seen.Add(idea.Idea.Name);
            yield return idea;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The plan: what one run writes.

    private sealed record Target(Guid Id, string Name, BaseUnit Unit, Guid? UsualStore);

    public ImportPlan Plan(ImportChoices choices, DateOnly today, Func<Guid> newId)
    {
        List<ImportEdit>        edits     = [];
        List<string>            leftOut   = [];
        List<NewIngredientLine> fresh     = [];
        int alreadyHere = 0, newCategories = 0, newGroups = 0, newMeals = 0, newLines = 0, newProducts = 0, newPrices = 0;

        // Categories, then meal groups: a heading is an existing one, a new one, or (deleted here) none.
        Dictionary<string, Guid?> categories = Headings(
            _sheet.CostHeaders.Where(h => !h.Household && _sheet.Costs.Any(c => Names.Same(c.Header, h.Header))).Select(h => h.Header),
            _known.Categories, choices.Categories, CategoryRules.Entity, CategoryRules.NameField, CategoryRules.PositionField,
            "shopping category", edits, leftOut, ref newCategories, newId);
        Dictionary<string, Guid?> groups = Headings(
            _sheet.MealHeaders.Where(h => NewMealIdeas().Any(m => Names.Same(m.Header, h))),
            _known.MealGroups, choices.MealGroups, MealGroupRules.Entity, MealGroupRules.NameField, MealGroupRules.PositionField,
            "meal group", edits, leftOut, ref newGroups, newId);

        // Ingredients: the app's by key; the sheet's that are new; then each meal's that are neither.
        Dictionary<string, Target> targets = _live.ToDictionary(l => l.Key,
            l => new Target(l.Value.Id, l.Value.Name, l.Value.Unit, l.Value.UsualStoreId), StringComparer.Ordinal);
        Dictionary<string, string?> suggested = Review().Ingredients.ToDictionary(q => q.Key, q => q.Suggested, StringComparer.Ordinal);
        string? Answer(string key) => choices.Ingredients.TryGetValue(key, out string? answer) ? answer : suggested.GetValueOrDefault(key);

        foreach (SheetIngredient item in _sheetIngredients)
        {
            if (_live.ContainsKey(item.Key))
            {
                alreadyHere++;
                continue;
            }
            if (_gone.Contains(item.Key))
            {
                leftOut.Add($"{item.Name}: deleted here before, so not added again.");
                continue;
            }
            // Answered as an ingredient already here ("Natural yoghurt" is "Greek yoghurt"), now or on an earlier import:
            // its prices go to that one.
            if ((KnownByProduct(item) ?? Answer(item.Key)) is { } same && _live.ContainsKey(same))
            {
                targets[item.Key] = targets[same];
                alreadyHere++;
                continue;
            }
            Guid? category = item.Category is { } header ? categories.GetValueOrDefault(header) : null;
            targets[item.Key] = NewIngredient(item.Key, item.Name, item.Category, category, _known.StoreFor(item.UsualStore),
                [.. item.Prices.Select(p => p.Price.Pack)], choices, edits, fresh, newId);
        }

        // An ingredient here whose unit is still the import's guess, and the sheet's price is counted another way: asked;
        // changed only when the planner chose the other unit.
        List<UnitQuestion> unitQuestions = [];
        int changedUnits = 0;
        Dictionary<Guid, KnownIngredient> liveById = _live.Values.ToDictionary(i => i.Id);
        foreach (SheetIngredient item in _sheetIngredients)
        {
            if (!targets.TryGetValue(item.Key, out Target? target) || !liveById.TryGetValue(target.Id, out KnownIngredient? here)
                || !here.UnitGuessed || unitQuestions.Any(q => q.Key == ImportNames.Key(here.Name)))
                continue;
            foreach (PriceCell cell in item.Prices)
            {
                string? pack = choices.PricePacks.TryGetValue(cell.Id, out string? typed) ? typed : cell.Price.Pack;
                if (pack is null || PackSize.Parse(pack, here.Unit, out _) is not null || UnitOfPack(pack) is not { } says)
                    continue;
                string key = ImportNames.Key(here.Name);
                unitQuestions.Add(new UnitQuestion(key, here.Name, here.Unit, says, $"{Money.Format(cell.Price.Cents)} for {pack}"));
                if (choices.Units.TryGetValue(key, out BaseUnit chosen) && chosen != here.Unit)
                {
                    edits.Add(new(IngredientRules.Entity, here.Id, OpKind.Update,
                    [
                        new OpField(IngredientRules.BaseUnitField, BaseUnits.Format(chosen)),
                        new OpField(IngredientRules.UnitGuessedField, OpValues.FormatBool(false)),
                    ]));
                    changedUnits++;
                    foreach (string k in targets.Where(t => t.Value.Id == here.Id).Select(t => t.Key).ToList())
                        targets[k] = targets[k] with { Unit = chosen };
                }
                break;
            }
        }

        foreach ((string key, string name, _) in MealRefs().DistinctBy(r => r.Key).ToList())
        {
            if (targets.ContainsKey(key) || _gone.Contains(key))
                continue;
            string? chosen = Answer(key);
            if (chosen is not null && targets.TryGetValue(chosen, out Target? same))
                targets[key] = same;
            else
                targets[key] = NewIngredient(key, ImportNames.Tidy(name), null, null, null, [], choices, edits, fresh, newId);
        }

        // Meals and their ingredients.
        List<string> seen = [];
        foreach (MealIdeaCell cell in _sheet.MealIdeas)
        {
            MealIdea idea = cell.Idea;
            if (seen.Any(s => Names.Same(s, idea.Name)))
            {
                leftOut.Add($"{idea.Name} ({cell.Place}): in the meal ideas twice - added once.");
                continue;
            }
            seen.Add(idea.Name);
            if (_known.Meals.FirstOrDefault(m => Names.Same(m.Name, idea.Name)) is { } known)
            {
                if (known.Deleted)
                    leftOut.Add($"{idea.Name}: deleted here before, so not added again.");
                else
                    alreadyHere++;
                continue;
            }

            int? serves = choices.Serves.TryGetValue(MealKey(idea.Name), out int? chosen) ? chosen : idea.Serves;
            MealKind kind = serves >= 2 ? MealKind.Cooked : MealKind.Simple;
            string? notes = idea.Unsure || idea.Trailing is not null
                ? MealRules.NormaliseNotes(Clip($"From the spreadsheet: {idea.Original}", MealRules.MaxNotesLength))
                : null;

            Guid meal = newId();
            edits.Add(new(MealRules.Entity, meal, OpKind.Create,
                new MealDraft(idea.Name, groups.GetValueOrDefault(cell.Header), kind, kind == MealKind.Cooked ? Math.Min(serves!.Value, MealRules.MaxServes) : 1, null, notes)
                    .FieldsChangedFrom(null)));
            newMeals++;

            List<Guid> used = [];
            IEnumerable<(string Key, string Name, string? Note)> lines = idea.Ingredients is { } refs
                ? refs.Select(r => (ImportNames.Key(r.Name), r.Name, LineNote(r)))
                : targets.ContainsKey(ImportNames.Key(idea.Name)) ? [(ImportNames.Key(idea.Name), idea.Name, null)] : [];
            foreach ((string key, string name, string? note) in lines)
            {
                if (!targets.TryGetValue(key, out Target? ingredient))
                {
                    if (_gone.Contains(key))
                        leftOut.Add($"{name} in {idea.Name}: that ingredient was deleted here.");
                    continue;
                }
                if (used.Contains(ingredient.Id))
                    continue;
                used.Add(ingredient.Id);
                edits.Add(new(MealIngredientRules.Entity, newId(), OpKind.Create,
                    new MealIngredientDraft(meal, ingredient.Id, null, note).FieldsChangedFrom(null)));
                newLines++;
            }
        }

        // Products and prices. Products tombstones and all, live ones first: one the planner deleted is not made again.
        HashSet<string>    samePrices = new(StringComparer.Ordinal);
        List<KnownProduct> products   = [.. _known.Products.OrderBy(p => p.Deleted)];
        List<(PriceCell Cell, KnownProduct Product)> priced = [];

        // A price's store: the planner's answer for it, else the store the sheet names beside it, else (an ingredient's)
        // usual store or (a household item's) the household store, else the one store for prices that name none.
        void Price(PriceCell cell, string item, Guid? ingredient, BaseUnit? unit, Guid? fallbackStore)
        {
            string what = $"{item} {Money.Format(cell.Price.Cents)} ({cell.Place})";
            Guid? store = choices.PriceStores.TryGetValue(cell.Id, out Guid? chosenStore)
                ? chosenStore
                : _known.StoreFor(cell.Price.Store) ?? fallbackStore ?? choices.PriceStore;
            if (store is not { } storeId)
            {
                leftOut.Add($"{what}: no store chosen.");
                return;
            }
            string? packText = choices.PricePacks.TryGetValue(cell.Id, out string? chosenPack) ? chosenPack : cell.Price.Pack;
            if (string.IsNullOrWhiteSpace(packText))
            {
                leftOut.Add(cell.Price.Note is { } note
                    ? $"{what}: how much is in the pack was not given - the sheet says \"{note}\"."
                    : $"{what}: how much is in the pack was not given.");
                return;
            }
            // A household item has no unit: its pack is a count, or what the pack says ("100g").
            if (PackSize.Parse(packText, unit ?? UnitOfPack(packText) ?? BaseUnit.Each, out string? problem) is not { } pack)
            {
                leftOut.Add($"{what}: the pack \"{packText}\" - {problem}");
                return;
            }

            KnownProduct? product = products.FirstOrDefault(p =>
                p.StoreId == storeId && p.PackQuantity == pack
                && (ingredient is { } id ? p.IngredientId == id : p.IngredientId is null && Names.Same(p.Name, item)));
            if (product is { Deleted: true })
            {
                leftOut.Add($"{what}: its product was deleted here, so not added again.");
                return;
            }
            if (product is null)
            {
                product = new KnownProduct(newId(), item, storeId, ingredient, pack, false);
                products.Add(product);
                edits.Add(new(ProductRules.Entity, product.Id, OpKind.Create,
                    new ProductDraft(product.Name, storeId, null, pack, ingredient).FieldsChangedFrom(null)));
                newProducts++;
            }
            priced.Add((cell, product));
        }

        foreach (SheetIngredient item in _sheetIngredients)
            if (targets.TryGetValue(item.Key, out Target? ingredient))
                foreach (PriceCell cell in item.Prices)
                    // Named as the sheet names it, which is how a later import knows the answer (KnownByProduct).
                    Price(cell, item.Name, ingredient.Id, ingredient.Unit, ingredient.UsualStore);

        foreach ((string name, IReadOnlyList<PriceCell> cells) in _household)
        {
            foreach (PriceCell cell in cells)
                Price(cell, name, null, null, choices.HouseholdStore);
            if (cells.Count > 0)
                continue;

            // Not priced yet: a product the planner can price later on the Prices page, at the household store.
            List<KnownProduct> named = [.. products.Where(p => p.IngredientId is null && Names.Same(p.Name, name))];
            if (named.Any(p => !p.Deleted))
                alreadyHere++;
            else if (named.Count > 0)
                leftOut.Add($"{name}: deleted here before, so not added again.");
            else if (choices.HouseholdStore is not { } store)
                leftOut.Add($"{name}: a household item - choose where household items are bought to add it.");
            else
            {
                KnownProduct product = new(newId(), name, store, null, 1, false);
                products.Add(product);
                edits.Add(new(ProductRules.Entity, product.Id, OpKind.Create, new ProductDraft(name, store, null, 1, null).FieldsChangedFrom(null)));
                newProducts++;
            }
        }

        // A product's prices are written when the sheet now says something it did not say last time - compared with the
        // sheet's own earlier prices (deleted ones too: a price the planner deleted is not said again), never with a
        // receipt or a typed price, which an unchanged sheet must not overwrite. Then all of them, so "what the sheet
        // said last" is always its whole say: a product with two prices in the sheet stays at two, run after run.
        foreach (IGrouping<Guid, (PriceCell Cell, KnownProduct Product)> product in priced.GroupBy(p => p.Product.Id))
        {
            HashSet<long> says = [.. product.Select(p => p.Cell.Price.Cents)];
            if (says.IsSubsetOf(SheetSaidLast(product.Key)))
            {
                alreadyHere += product.Count();
                samePrices.UnionWith(product.Select(p => p.Cell.Id));
                continue;
            }
            foreach (long cents in says)
            {
                edits.Add(new(PriceRules.Entity, newId(), OpKind.Create,
                    new PriceEntry(product.Key, cents, today, PriceSource.Sheet, null).Fields()));
                newPrices++;
            }
        }

        return new ImportPlan(edits, fresh, newCategories, newGroups, newMeals, newLines, newProducts, newPrices, alreadyHere, leftOut,
            unitQuestions, changedUnits, samePrices);
    }

    /// <summary>A meal ingredient's note: what was in its own parentheses, then its alternatives - "basmati; or brown".</summary>
    private static string? LineNote(IngredientRef line)
    {
        string note = string.Join("; ", new[]
        {
            line.Note,
            line.Alternatives.Count > 0 ? "or " + string.Join(" or ", line.Alternatives) : null,
        }.OfType<string>());
        return MealIngredientRules.NormaliseNote(Clip(note, MealIngredientRules.MaxNoteLength));
    }

    /// <summary>The prices the sheet gave a product on the last day it gave any - tombstones included.</summary>
    private HashSet<long> SheetSaidLast(Guid product)
    {
        List<KnownPrice> said = [.. _known.Prices.Where(p => p.ProductId == product && p.Source == PriceSource.Sheet)];
        if (said.Count == 0)
            return [];
        DateOnly last = said.Max(p => p.ObservedOn);
        return [.. said.Where(p => p.ObservedOn == last).Select(p => p.PriceCents)];
    }

    private Target NewIngredient(
        string key, string name, string? categoryHeading, Guid? category, Guid? store, IReadOnlyList<string?> packs,
        ImportChoices choices, List<ImportEdit> edits, List<NewIngredientLine> fresh, Func<Guid> newId)
    {
        bool     chosen = choices.Units.TryGetValue(key, out BaseUnit unit);
        if (!chosen)
            unit = GuessUnit(name, categoryHeading, packs);

        Guid id = newId();
        edits.Add(new(IngredientRules.Entity, id, OpKind.Create,
            new IngredientDraft(name, unit, category, store, false, UnitGuessed: !chosen).FieldsChangedFrom(null)));
        fresh.Add(new NewIngredientLine(key, name, unit, !chosen, categoryHeading));
        return new Target(id, name, unit, store);
    }

    /// <summary>
    /// A pack the sheet gave says more than the name does: "2kg" is grams, "2L" millilitres, "24" a count. Otherwise
    /// <see cref="UnitGuess"/>.
    /// </summary>
    public static BaseUnit GuessUnit(string name, string? category, IReadOnlyList<string?> packs) =>
        packs.Select(p => p is null ? null : UnitOfPack(p)).FirstOrDefault(u => u is not null) ?? UnitGuess.For(name, category);

    /// <summary>What a pack is counted in: "2kg" grams, "2L" millilitres, "24" or "18pk" each; null when it is no pack.</summary>
    public static BaseUnit? UnitOfPack(string pack)
    {
        bool hasLetters = pack.Any(char.IsLetter);
        foreach (BaseUnit unit in new[] { BaseUnit.Gram, BaseUnit.Millilitre, BaseUnit.Each })
            if ((hasLetters || unit == BaseUnit.Each) && PackSize.Parse(pack, unit, out _) is not null)
                return unit;
        return null;
    }

    private static Dictionary<string, Guid?> Headings(
        IEnumerable<string> headings, IReadOnlyList<KnownOrdered> known, Dictionary<string, Guid?> answers,
        string entity, string nameField, string positionField, string noun,
        List<ImportEdit> edits, List<string> leftOut, ref int created, Func<Guid> newId)
    {
        Dictionary<string, Guid?> ids       = new(StringComparer.OrdinalIgnoreCase);
        List<decimal>             positions = [.. known.Where(k => !k.Deleted).Select(k => k.Position)];
        foreach (string heading in headings)
        {
            if (ids.ContainsKey(heading))
                continue;

            Guid? chosen;
            if (answers.TryGetValue(heading, out Guid? answer))
                chosen = answer;
            else
            {
                (chosen, bool same) = SuggestHeading(heading, known);
                if (chosen is null && same)
                {
                    leftOut.Add($"{heading}: that {noun} was deleted here, so what is under it has none.");
                    ids[heading] = null;
                    continue;
                }
            }

            if (chosen is { } id && known.Any(k => k.Id == id && !k.Deleted))
            {
                ids[heading] = id;
                continue;
            }

            Guid    fresh    = newId();
            decimal position = ListPosition.AtEnd(positions);
            positions.Add(position);
            edits.Add(new(entity, fresh, OpKind.Create,
            [
                new OpField(nameField, Clip(Names.Normalise(heading), Names.MaxLength)),
                new OpField(positionField, ListPosition.Format(position)),
            ]));
            ids[heading] = fresh;
            created++;
        }
        return ids;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();
}
