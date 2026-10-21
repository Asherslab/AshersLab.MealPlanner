using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Shared.Domain.Import;

public sealed record KnownIngredient(Guid Id, string Name, BaseUnit Unit, bool Deleted);

public sealed record KnownOrdered(Guid Id, string Name, decimal Position, bool Deleted);

public sealed record KnownNamed(Guid Id, string Name, bool Deleted);

public sealed record KnownProduct(Guid Id, string Name, Guid StoreId, Guid? IngredientId, decimal PackQuantity, bool Deleted);

public sealed record KnownPrice(Guid Id, Guid ProductId, long PriceCents, DateOnly ObservedOn, bool Deleted);

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

    /// <summary>A meal idea's ingredient (by <see cref="ImportNames.Key"/>): the key of the ingredient it is, or null for a new one.</summary>
    public Dictionary<string, string?> Ingredients { get; } = new(StringComparer.Ordinal);

    /// <summary>A new ingredient's unit, chosen in the review - no longer a guess.</summary>
    public Dictionary<string, BaseUnit> Units { get; } = new(StringComparer.Ordinal);

    /// <summary>A price (<see cref="PriceCell.Id"/>): the store it is at, or null to leave it out.</summary>
    public Dictionary<string, Guid?> PriceStores { get; } = new(StringComparer.Ordinal);

    /// <summary>A price: what it is for, as typed ("24", "2kg"), or null to leave it out.</summary>
    public Dictionary<string, string?> PricePacks { get; } = new(StringComparer.Ordinal);

    /// <summary>A meal (by <see cref="SheetImport.MealKey"/>): the serves a batch makes, or null for a single serve.</summary>
    public Dictionary<string, int?> Serves { get; } = new(StringComparer.Ordinal);

    /// <summary>Where the non-food items are bought. Null: those with no store of their own are left out.</summary>
    public Guid? HouseholdStore { get; set; }
}

/// <summary>A heading that is not obviously one the app has: "Snacks/desserts" for the group "Snack".</summary>
public sealed record HeadingQuestion(string Heading, IReadOnlyList<KnownOrdered> Options, Guid? Suggested);

/// <summary>An ingredient a meal names that is not exactly one the sheet or the app has: "cheese".</summary>
public sealed record IngredientQuestion(string Key, string Name, IReadOnlyList<string> Meals, IReadOnlyList<(string Key, string Name)> Options, string? Suggested);

/// <summary>A meal whose serves the sheet does not say for sure.</summary>
public sealed record ServesQuestion(string Key, string Meal, int? Suggested, string Why);

/// <summary>One price, and what is known of it - the review asks for the store and pack when the sheet does not say.</summary>
public sealed record PriceQuestion(string Id, SheetPlace Place, string Item, bool Household, long Cents, string? Pack, Guid? Store);

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
    IReadOnlyList<string> LeftOut)
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
/// matches is left alone, never updated: the planner may have fixed it since. Something deleted here is not brought
/// back. A product is the same one when its store, its ingredient (or, for a household item, its name) and its pack
/// size match; a price is written only when it differs from the product's latest. So a later download of the sheet,
/// with more prices filled in, adds exactly those prices.
/// </remarks>
public sealed class SheetImport
{
    private readonly ParsedSheet _sheet;
    private readonly ImportKnown _known;

    private readonly Dictionary<string, KnownIngredient> _live = new(StringComparer.Ordinal);
    private readonly HashSet<string>                     _gone = new(StringComparer.Ordinal);
    private readonly List<SheetIngredient>               _sheetIngredients = [];
    private readonly List<(string Name, IReadOnlyList<PriceCell> Prices)> _household = [];

    private sealed record SheetIngredient(string Key, string Name, string? Category, string? UsualStore, List<PriceCell> Prices);

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
            AddSheetIngredient(item.Name, item.Header, item.UsualStore, item.Prices);
        }
        foreach (FlatItemCell item in sheet.Flat)
            AddSheetIngredient(item.Name, null, null, item.Prices);
    }

    private void AddSheetIngredient(string name, string? category, string? usualStore, IReadOnlyList<PriceCell> prices)
    {
        string key = ImportNames.Key(name);
        if (key.Length == 0)
            return;
        SheetIngredient? known = _sheetIngredients.FirstOrDefault(i => i.Key == key);
        if (known is null)
            _sheetIngredients.Add(new SheetIngredient(key, name, category, usualStore, [.. prices]));
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
                prices.Add(new PriceQuestion(price.Id, price.Place, item.Name, false, price.Price.Cents, price.Price.Pack, _known.StoreFor(price.Price.Store)));
        foreach ((string name, IReadOnlyList<PriceCell> cells) in _household)
            foreach (PriceCell price in cells)
                prices.Add(new PriceQuestion(price.Id, price.Place, name, true, price.Price.Cents, price.Price.Pack, _known.StoreFor(price.Price.Store)));

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

    private sealed record Target(Guid Id, string Name, BaseUnit Unit);

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
        Dictionary<string, Target> targets = _live.ToDictionary(l => l.Key, l => new Target(l.Value.Id, l.Value.Name, l.Value.Unit), StringComparer.Ordinal);
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
            Guid? category = item.Category is { } header ? categories.GetValueOrDefault(header) : null;
            targets[item.Key] = NewIngredient(item.Key, item.Name, item.Category, category, _known.StoreFor(item.UsualStore),
                [.. item.Prices.Select(p => p.Price.Pack)], choices, edits, fresh, newId);
        }

        Dictionary<string, string?> suggested = Review().Ingredients.ToDictionary(q => q.Key, q => q.Suggested, StringComparer.Ordinal);
        foreach ((string key, string name, _) in MealRefs().DistinctBy(r => r.Key).ToList())
        {
            if (targets.ContainsKey(key) || _gone.Contains(key))
                continue;
            string? chosen = choices.Ingredients.TryGetValue(key, out string? answer) ? answer : suggested.GetValueOrDefault(key);
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
                ? refs.Select(r => (ImportNames.Key(r.Name), r.Name,
                    r.Alternatives.Count > 0 ? MealIngredientRules.NormaliseNote(Clip("or " + string.Join(" or ", r.Alternatives), MealIngredientRules.MaxNoteLength)) : null))
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

        // Products and prices.
        List<KnownProduct> products = [.. _known.Products.Where(p => !p.Deleted)];
        Dictionary<Guid, long> latest = _known.Prices
            .Where(p => !p.Deleted)
            .GroupBy(p => p.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.ObservedOn).ThenByDescending(p => p.Id).First().PriceCents);

        void Price(PriceCell cell, string item, Guid? ingredient, BaseUnit unit, Guid? fallbackStore)
        {
            string what = $"{item} {Money.Format(cell.Price.Cents)} ({cell.Place})";
            Guid? store = choices.PriceStores.TryGetValue(cell.Id, out Guid? chosenStore) ? chosenStore : _known.StoreFor(cell.Price.Store) ?? fallbackStore;
            if (store is not { } storeId)
            {
                leftOut.Add($"{what}: no store chosen.");
                return;
            }
            string? packText = choices.PricePacks.TryGetValue(cell.Id, out string? chosenPack) ? chosenPack : cell.Price.Pack;
            if (string.IsNullOrWhiteSpace(packText))
            {
                leftOut.Add($"{what}: how much is in the pack was not given.");
                return;
            }
            if (PackSize.Parse(packText, unit, out string? problem) is not { } pack)
            {
                leftOut.Add($"{what}: the pack \"{packText}\" - {problem}");
                return;
            }

            KnownProduct? product = products.FirstOrDefault(p =>
                p.StoreId == storeId && p.PackQuantity == pack
                && (ingredient is { } id ? p.IngredientId == id : p.IngredientId is null && Names.Same(p.Name, item)));
            if (product is null)
            {
                product = new KnownProduct(newId(), item, storeId, ingredient, pack, false);
                products.Add(product);
                edits.Add(new(ProductRules.Entity, product.Id, OpKind.Create,
                    new ProductDraft(product.Name, storeId, null, pack, ingredient).FieldsChangedFrom(null)));
                newProducts++;
            }

            if (latest.TryGetValue(product.Id, out long cents) && cents == cell.Price.Cents)
            {
                alreadyHere++;
                return;
            }
            latest[product.Id] = cell.Price.Cents;
            edits.Add(new(PriceRules.Entity, newId(), OpKind.Create,
                new PriceEntry(product.Id, cell.Price.Cents, today, PriceSource.Manual, null).Fields()));
            newPrices++;
        }

        foreach (SheetIngredient item in _sheetIngredients)
            if (targets.TryGetValue(item.Key, out Target? ingredient))
                foreach (PriceCell cell in item.Prices)
                    Price(cell, ingredient.Name, ingredient.Id, ingredient.Unit, null);

        foreach ((string name, IReadOnlyList<PriceCell> cells) in _household)
        {
            foreach (PriceCell cell in cells)
                Price(cell, name, null, BaseUnit.Each, choices.HouseholdStore);
            if (cells.Count > 0)
                continue;

            // Not priced yet: a product the planner can price later on the Prices page, at the household store.
            if (products.Any(p => p.IngredientId is null && Names.Same(p.Name, name)))
                alreadyHere++;
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

        return new ImportPlan(edits, fresh, newCategories, newGroups, newMeals, newLines, newProducts, newPrices, alreadyHere, leftOut);
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
        return new Target(id, name, unit);
    }

    /// <summary>
    /// A pack the sheet gave says more than the name does: "2kg" is grams, "2L" millilitres, "24" a count. Otherwise
    /// <see cref="UnitGuess"/>.
    /// </summary>
    public static BaseUnit GuessUnit(string name, string? category, IReadOnlyList<string?> packs)
    {
        foreach (string? pack in packs)
        {
            if (pack is null)
                continue;
            foreach (BaseUnit unit in new[] { BaseUnit.Gram, BaseUnit.Millilitre, BaseUnit.Each })
            {
                bool hasLetters = pack.Any(char.IsLetter);
                if ((hasLetters || unit == BaseUnit.Each) && PackSize.Parse(pack, unit, out _) is not null)
                    return unit;
            }
        }
        return UnitGuess.For(name, category);
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
