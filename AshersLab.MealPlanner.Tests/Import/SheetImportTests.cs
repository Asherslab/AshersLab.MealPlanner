using AshersLab.MealPlanner.Shared.Domain.Categories;
using AshersLab.MealPlanner.Shared.Domain.Import;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Import;

/// <summary>The import's decisions against a database holding only the seeds, or what a test adds to it.</summary>
public class SheetImportTests
{
    public static readonly Guid Woolworths = Guid.Parse("00000000-5eed-7000-8000-000000000101");
    public static readonly Guid Costco     = Guid.Parse("00000000-5eed-7000-8000-000000000102");
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static readonly string[] CategorySeeds = ["Meat", "Dairy", "Fruit", "Vegetables", "Carbs/sides", "Pantry", "Fridge", "Freezer", "Household"];
    private static readonly string[] GroupSeeds    = ["Beef", "Chicken & egg", "Fish", "Other", "Individual", "Side", "Snack"];

    public static ImportKnown Seeds(
        IEnumerable<KnownIngredient>? ingredients = null, IEnumerable<KnownNamed>? meals = null,
        IEnumerable<KnownProduct>? products = null, IEnumerable<KnownPrice>? prices = null, IEnumerable<KnownOrdered>? categories = null) =>
        new(
            [.. ingredients ?? []],
            [.. CategorySeeds.Select((n, i) => new KnownOrdered(Seed(200 + i + 1), n, i + 1, false)), .. categories ?? []],
            [.. GroupSeeds.Select((n, i) => new KnownOrdered(Seed(300 + i + 1), n, i + 1, false))],
            [.. meals ?? []],
            [new KnownNamed(Woolworths, "Woolworths", false), new KnownNamed(Costco, "Costco", false)],
            [.. products ?? []],
            [.. prices ?? []]);

    public static Guid Seed(int n) => Guid.Parse($"00000000-5eed-7000-8000-{n:000000000000}");

    private static SheetImport Import(ImportKnown known) =>
        new(SpreadsheetLayout.Parse(SyntheticSheet.Both(), known.StoreWords), known);

    private static ImportPlan Plan(ImportKnown known, ImportChoices? choices = null) =>
        Import(known).Plan(choices ?? new ImportChoices(), Today, Guid.CreateVersion7);

    private static IEnumerable<ImportEdit> Of(ImportPlan plan, string entity) => plan.Edits.Where(e => e.Entity == entity);

    /// <summary>The value an edit sends for a field; null when it sends none (or sends null).</summary>
    private static string? Field(ImportEdit edit, string name) => edit.Fields.SingleOrDefault(f => f.Name == name)?.Value;

    private static ImportEdit Named(ImportPlan plan, string entity, string name) =>
        Of(plan, entity).Single(e => e.Fields.Any(f => f.Name == "Name" && f.Value == name));

    [Fact]
    public void Every_ingredient_once_however_the_sheet_spells_it()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Equal(
            ["Beef mince", "Cheese", "Chicken thighs", "Chives", "Eggs", "Gravy", "Ham", "Korma paste", "Lamb mince", "Pears",
             "Peas", "Popcorn", "Potatoes", "Rice", "Shredded cheese", "Swiss cheese", "Yoghurt"],
            plan.NewIngredients.Select(i => i.Name).Order());
        Assert.All(Of(plan, IngredientRules.Entity), e => Assert.Equal(OpKind.Create, e.Kind));
    }

    [Fact]
    public void Every_unit_is_marked_as_a_guess_unless_the_planner_chose_it()
    {
        ImportChoices choices = new();
        choices.Units["gravy"] = BaseUnit.Millilitre;
        ImportPlan plan = Plan(Seeds(), choices);

        Assert.Equal("true", Field(Named(plan, IngredientRules.Entity, "Lamb mince"), IngredientRules.UnitGuessedField));
        Assert.Equal("Gram", Field(Named(plan, IngredientRules.Entity, "Lamb mince"), IngredientRules.BaseUnitField));
        // The pack "12" the sheet gave says eggs are counted.
        Assert.Equal("Each", Field(Named(plan, IngredientRules.Entity, "Eggs"), IngredientRules.BaseUnitField));
        ImportEdit gravy = Named(plan, IngredientRules.Entity, "Gravy");
        Assert.Equal("Millilitre", Field(gravy, IngredientRules.BaseUnitField));
        Assert.DoesNotContain(gravy.Fields, f => f.Name == IngredientRules.UnitGuessedField);
    }

    [Fact]
    public void Ingredients_go_in_the_category_of_their_heading_and_the_store_beside_their_name()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Equal(OpValues.FormatId(Seed(201)), Field(Named(plan, IngredientRules.Entity, "Lamb mince"), IngredientRules.CategoryField));
        Assert.Equal(OpValues.FormatId(Woolworths), Field(Named(plan, IngredientRules.Entity, "Pears"), IngredientRules.UsualStoreField));
        // Only on the flat list: no category.
        Assert.DoesNotContain(Named(plan, IngredientRules.Entity, "Chives").Fields, f => f.Name == IngredientRules.CategoryField);
        Assert.Empty(Of(plan, CategoryRules.Entity));
    }

    [Fact]
    public void Meals_get_their_serves_kind_group_and_ingredients_with_no_quantities()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Equal(7, plan.NewMeals);
        ImportEdit shepherd = Named(plan, MealRules.Entity, "Shepherd pie");
        Assert.Equal("Cooked", Field(shepherd, MealRules.KindField));
        Assert.Equal("4", Field(shepherd, MealRules.ServesPerBatchField));
        Assert.Equal(OpValues.FormatId(Seed(301)), Field(shepherd, MealRules.GroupField));

        List<ImportEdit> lines = [.. Of(plan, MealIngredientRules.Entity).Where(l => Field(l, MealIngredientRules.MealField) == OpValues.FormatId(shepherd.Id))];
        Assert.Equal(4, lines.Count);
        Assert.All(lines, l => Assert.DoesNotContain(l.Fields, f => f.Name == MealIngredientRules.QuantityField));

        // A heading spelt differently is the seeded group; one the app has no group for is a new one, at the end.
        Assert.Equal(OpValues.FormatId(Seed(302)), Field(Named(plan, MealRules.Entity, "Omelette"), MealRules.GroupField));
        Assert.Equal(OpValues.FormatId(Seed(306)), Field(Named(plan, MealRules.Entity, "Garlic naan"), MealRules.GroupField));
        ImportEdit fruit = Named(plan, MealGroupRules.Entity, "Fruit");
        Assert.Equal("8", Field(fruit, MealGroupRules.PositionField));
        Assert.Equal(OpValues.FormatId(fruit.Id), Field(Named(plan, MealRules.Entity, "Pears"), MealRules.GroupField));
    }

    [Fact]
    public void A_meal_with_no_parentheses_is_a_single_serve_made_of_the_ingredient_it_is_named_for()
    {
        ImportPlan plan = Plan(Seeds());

        ImportEdit pears = Named(plan, MealRules.Entity, "Pears");
        Assert.Equal("Simple", Field(pears, MealRules.KindField));
        Assert.Equal("1", Field(pears, MealRules.ServesPerBatchField));
        Assert.Single(Of(plan, MealIngredientRules.Entity), l => Field(l, MealIngredientRules.MealField) == OpValues.FormatId(pears.Id));
        // Nothing called garlic naan to buy: a meal with no ingredients yet.
        ImportEdit naan = Named(plan, MealRules.Entity, "Garlic naan");
        Assert.DoesNotContain(Of(plan, MealIngredientRules.Entity), l => Field(l, MealIngredientRules.MealField) == OpValues.FormatId(naan.Id));
    }

    [Fact]
    public void An_unsure_cell_keeps_what_the_sheet_said_in_the_notes()
    {
        ImportPlan plan = Plan(Seeds());

        ImportEdit korma = Named(plan, MealRules.Entity, "Lamb korma");
        Assert.Equal("6", Field(korma, MealRules.ServesPerBatchField));
        Assert.StartsWith("From the spreadsheet: 6?x Lamb korma (chicken thighs", Field(korma, MealRules.NotesField));
        Assert.Equal("From the spreadsheet: Popcorn?", Field(Named(plan, MealRules.Entity, "Popcorn"), MealRules.NotesField));
        Assert.DoesNotContain(Named(plan, MealRules.Entity, "Shepherd pie").Fields, f => f.Name == MealRules.NotesField);
    }

    [Fact]
    public void A_meal_in_the_list_twice_is_added_once_and_said_so()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Single(Of(plan, MealRules.Entity), e => Field(e, MealRules.NameField) == "Shepherd pie");
        Assert.Contains(plan.LeftOut, l => l.StartsWith("Shepherd pie (Plan B13): in the meal ideas twice"));
    }

    [Fact]
    public void An_alternative_ingredient_is_a_note_on_the_first()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Single(Of(plan, MealIngredientRules.Entity), l => Field(l, MealIngredientRules.NoteField) == "or ham");
    }

    [Fact]
    public void The_review_asks_about_what_it_cannot_tell()
    {
        ImportReview review = Import(Seeds()).Review();

        IngredientQuestion cheese = review.Ingredients.Single();
        Assert.Equal("cheese", cheese.Key);
        Assert.Equal(["Omelette"], cheese.Meals);
        Assert.Equal(["Shredded cheese", "Swiss cheese"], cheese.Options.Select(o => o.Name).Order());
        Assert.Null(cheese.Suggested);

        Assert.Equal([("Lamb korma", 6), ("Cottage pie", (int?)null)], review.Serves.Select(s => (s.Meal, s.Suggested)));
        // Exactly a seed's name is not asked; a different spelling is, with the seed suggested.
        Assert.Empty(review.Categories);
        Assert.Equal(["Fruit"], review.MealGroups.Select(q => q.Heading));
        Assert.Null(review.MealGroups.Single().Suggested);
        Assert.Equal(["Toilet rolls", "Paper towel"], review.HouseholdItems);
    }

    [Fact]
    public void An_ingredient_like_nothing_known_is_not_asked_about_only_added()
    {
        SheetImport import = new(SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "MEAL IDEAS|Beef",
            "|4x Ragu (pappardelle)")], ["Costco"]), Seeds());

        Assert.Empty(import.Review().Ingredients);
        Assert.Equal(["Pappardelle"], import.Plan(new ImportChoices(), Today, Guid.CreateVersion7).NewIngredients.Select(i => i.Name));
    }

    [Fact]
    public void A_heading_that_only_resembles_a_seed_is_asked_with_it_suggested()
    {
        SheetImport import = new(SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "MEAL IDEAS|Snacks/sweets",
            "|Fruit salad (pears)")], ["Costco"]), Seeds());

        HeadingQuestion question = import.Review().MealGroups.Single();
        Assert.Equal(Seed(307), question.Suggested);
        Assert.Equal(OpValues.FormatId(Seed(307)),
            Field(import.Plan(new ImportChoices(), Today, Guid.CreateVersion7).Edits.Single(e => e.Entity == MealRules.Entity), MealRules.GroupField));
    }

    [Fact]
    public void The_planners_answers_win_over_the_suggestions()
    {
        ImportChoices choices = new();
        choices.Ingredients["cheese"] = "shredded cheese";
        choices.Serves["cottage pie"]  = 4;
        choices.Serves["lamb korma"]   = null;
        choices.MealGroups["Fruit"]    = Seed(307);
        ImportPlan plan = Plan(Seeds(), choices);

        Assert.DoesNotContain(plan.NewIngredients, i => i.Name == "Cheese");
        ImportEdit grated = Named(plan, IngredientRules.Entity, "Shredded cheese");
        Assert.Contains(Of(plan, MealIngredientRules.Entity), l => Field(l, MealIngredientRules.IngredientField) == OpValues.FormatId(grated.Id));
        Assert.Equal("Cooked", Field(Named(plan, MealRules.Entity, "Cottage pie"), MealRules.KindField));
        Assert.Equal("Simple", Field(Named(plan, MealRules.Entity, "Lamb korma"), MealRules.KindField));
        Assert.Empty(Of(plan, MealGroupRules.Entity));
    }

    [Fact]
    public void A_price_is_imported_where_the_store_and_pack_are_known_and_the_rest_is_said()
    {
        ImportPlan plan = Plan(Seeds());

        Assert.Equal(2, plan.NewPrices);
        ImportEdit lamb = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Lamb mince");
        Assert.Equal(OpValues.FormatId(Costco), Field(lamb, ProductRules.StoreField));
        Assert.Equal("2000", Field(lamb, ProductRules.PackQuantityField));
        ImportEdit price = Of(plan, PriceRules.Entity).Single(p => Field(p, PriceRules.ProductField) == OpValues.FormatId(lamb.Id));
        Assert.Equal("2499", Field(price, PriceRules.PriceCentsField));
        Assert.Equal("Sheet", Field(price, PriceRules.SourceField));
        Assert.Equal("2026-10-21", Field(price, PriceRules.ObservedOnField));

        ImportEdit korma = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Korma paste");
        Assert.Equal(OpValues.FormatId(Woolworths), Field(korma, ProductRules.StoreField));

        Assert.Contains("Swiss cheese $9.50 (Plan D16): no store chosen.", plan.LeftOut);
        Assert.Contains("Chicken thighs $17.00 (Plan C18): how much is in the pack was not given.", plan.LeftOut);
        Assert.Contains("Toilet rolls $14.50 (Plan G16): no store chosen.", plan.LeftOut);
        Assert.Contains("Paper towel: a household item - choose where household items are bought to add it.", plan.LeftOut);
    }

    [Fact]
    public void The_review_fills_in_a_store_and_pack_and_household_items_become_products()
    {
        ImportChoices choices = new();
        choices.PriceStores["Plan D16 0"] = Costco;
        choices.PricePacks["Plan C18 0"]  = "1kg";
        choices.HouseholdStore            = Woolworths;
        ImportPlan plan = Plan(Seeds(), choices);

        Assert.Equal(5, plan.NewPrices);
        ImportEdit rolls = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Toilet rolls");
        Assert.Equal("24", Field(rolls, ProductRules.PackQuantityField));
        Assert.DoesNotContain(rolls.Fields, f => f.Name == ProductRules.IngredientField);
        ImportEdit towel = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Paper towel");
        Assert.Equal("1", Field(towel, ProductRules.PackQuantityField));
        Assert.DoesNotContain(plan.LeftOut, l => l.Contains("Toilet rolls") || l.Contains("Swiss") || l.Contains("Chicken thighs"));
    }

    [Fact]
    public void A_pack_of_the_wrong_kind_for_the_unit_is_left_out_with_why()
    {
        ImportChoices choices = new();
        choices.PricePacks["Plan C18 0"] = "1L";
        ImportPlan plan = Plan(Seeds(), choices);

        Assert.Contains(plan.LeftOut, l => l.StartsWith("Chicken thighs $17.00 (Plan C18): the pack \"1L\" - It is counted in g"));
    }

    [Fact]
    public void What_is_already_here_is_left_alone_and_what_was_deleted_here_is_not_brought_back()
    {
        Guid lamb = Guid.CreateVersion7();
        ImportKnown known = Seeds(
            ingredients: [new KnownIngredient(lamb, "lamb MINCE", BaseUnit.Each, false), new KnownIngredient(Guid.CreateVersion7(), "Chives", BaseUnit.Each, true)],
            meals: [new KnownNamed(Guid.CreateVersion7(), "shepherd pie", false), new KnownNamed(Guid.CreateVersion7(), "Pears", true)]);
        ImportPlan plan = Plan(known);

        Assert.DoesNotContain(plan.NewIngredients, i => i.Name is "Lamb mince" or "Chives");
        Assert.DoesNotContain(Of(plan, MealRules.Entity), m => Field(m, MealRules.NameField) is "Shepherd pie" or "Pears");
        Assert.Contains("Chives: deleted here before, so not added again.", plan.LeftOut);
        Assert.Contains("Pears: deleted here before, so not added again.", plan.LeftOut);
        Assert.Contains("chives in Omelette: that ingredient was deleted here.", plan.LeftOut);
        // The existing ingredient is counted in each, so the sheet's "2kg" is not its pack: said, not converted.
        Assert.Contains(plan.LeftOut, l => l.StartsWith("Lamb mince $24.99 (Plan C16): the pack \"2kg\""));
        Assert.DoesNotContain(plan.Edits, e => e.Id == lamb);
    }

    [Fact]
    public void A_price_the_sheet_said_last_time_is_not_written_again_whatever_came_since()
    {
        Guid lamb = Guid.CreateVersion7(), product = Guid.CreateVersion7();
        KnownProduct costcoLamb = new(product, "Lamb mince", Costco, lamb, 2000, false);
        // The sheet said $24.99 on 1 Oct; a receipt said $22.99 since. The unchanged sheet must not undo the receipt.
        ImportKnown same = Seeds(
            ingredients: [new KnownIngredient(lamb, "Lamb mince", BaseUnit.Gram, false)],
            products: [costcoLamb],
            prices: [new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 10, 1), false, PriceSource.Sheet),
                     new KnownPrice(Guid.CreateVersion7(), product, 2299, new DateOnly(2026, 10, 15), false, PriceSource.Receipt)]);
        Assert.DoesNotContain(Plan(same).Edits, e => e.Entity is ProductRules.Entity && Field(e, ProductRules.NameField) == "Lamb mince");
        Assert.DoesNotContain(Plan(same).Edits, e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));
        // The review says which, so it does not show "will be imported" for it.
        Assert.Equal(["Plan C16 0"], Plan(same).PricesAlreadyHere);

        // A typed or receipt price equal to the sheet's is not what the sheet said: the sheet's own is written.
        ImportKnown typed = same with
        {
            Prices = [new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 10, 1), false, PriceSource.Manual)],
        };
        ImportEdit written = Plan(typed).Edits.Single(e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));
        Assert.Equal("Sheet", Field(written, PriceRules.SourceField));

        // The sheet said $22.99 last time and says $24.99 now: news.
        ImportKnown changed = same with
        {
            Prices = [new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 9, 1), false, PriceSource.Sheet),
                      new KnownPrice(Guid.CreateVersion7(), product, 2299, new DateOnly(2026, 10, 1), false, PriceSource.Sheet)],
        };
        Assert.Single(Plan(changed).Edits, e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));
    }

    [Fact]
    public void Two_prices_for_one_product_are_both_written_once_and_never_again()
    {
        SheetImport Two(ImportKnown known) => new(SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "COSTS|Pantry",
            "|Oats 3.50/1kg Costco",
            "|oats 4.00/1kg Costco")], known.StoreWords), known);

        ImportPlan first = Two(Seeds()).Plan(new ImportChoices(), Today, Guid.CreateVersion7);
        ImportEdit product = Of(first, ProductRules.Entity).Single();
        Assert.Equal(["350", "400"], Of(first, PriceRules.Entity).Select(p => Field(p, PriceRules.PriceCentsField)).Order());

        ImportKnown after = Seeds(
            ingredients: [new KnownIngredient(Of(first, IngredientRules.Entity).Single().Id, "Oats", BaseUnit.Gram, false)],
            products: [new KnownProduct(product.Id, "Oats", Costco, Of(first, IngredientRules.Entity).Single().Id, 1000, false)],
            prices: [.. Of(first, PriceRules.Entity).Select(p => new KnownPrice(p.Id, product.Id,
                long.Parse(Field(p, PriceRules.PriceCentsField)!), Today, false, PriceSource.Sheet))]);
        Assert.True(Two(after).Plan(new ImportChoices(), Today.AddDays(7), Guid.CreateVersion7).NothingToDo);
    }

    [Fact]
    public void A_product_or_price_the_planner_deleted_is_not_brought_back()
    {
        Guid lamb = Guid.CreateVersion7(), product = Guid.CreateVersion7();
        ImportKnown deletedProduct = Seeds(
            ingredients: [new KnownIngredient(lamb, "Lamb mince", BaseUnit.Gram, false)],
            products: [new KnownProduct(product, "Lamb mince", Costco, lamb, 2000, true)]);
        ImportPlan plan = Plan(deletedProduct);
        Assert.DoesNotContain(plan.Edits, e => e.Entity is ProductRules.Entity && Field(e, ProductRules.IngredientField) == OpValues.FormatId(lamb));
        Assert.Contains("Lamb mince $24.99 (Plan C16): its product was deleted here, so not added again.", plan.LeftOut);

        ImportKnown deletedPrice = deletedProduct with
        {
            Products = [new KnownProduct(product, "Lamb mince", Costco, lamb, 2000, false)],
            Prices   = [new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 10, 1), true, PriceSource.Sheet)],
        };
        Assert.DoesNotContain(Plan(deletedPrice).Edits, e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));
        Assert.Contains("Plan C16 0", Plan(deletedPrice).PricesAlreadyHere);

        // A household item with no price, its product deleted: not made again either.
        ImportKnown deletedTowel = Seeds(products: [new KnownProduct(Guid.CreateVersion7(), "Paper towel", Costco, null, 1, true)]);
        ImportPlan towel = Plan(deletedTowel, new ImportChoices { HouseholdStore = Costco });
        Assert.DoesNotContain(towel.Edits, e => e.Entity is ProductRules.Entity && Field(e, ProductRules.NameField) == "Paper towel");
        Assert.Contains("Paper towel: deleted here before, so not added again.", towel.LeftOut);
    }

    [Fact]
    public void A_meal_ingredient_spelt_a_letter_off_is_asked_with_the_likely_one_chosen_and_taken_unanswered()
    {
        SheetImport import = new(SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
            "MEAL IDEAS|Beef",
            "|4x Ragu (pappardele, beef mince)",
            "COSTS|Pantry|Meat",
            "|Pappardelle|Beef mince")], ["Costco"]), Seeds());

        IngredientQuestion question = import.Review().Ingredients.Single();
        Assert.Equal(("pappardele", "pappardelle"), (question.Key, question.Suggested));

        ImportPlan plan = import.Plan(new ImportChoices(), Today, Guid.CreateVersion7);
        Assert.DoesNotContain(plan.NewIngredients, i => i.Name == "Pappardele");
        ImportEdit pasta = Named(plan, IngredientRules.Entity, "Pappardelle");
        Assert.Contains(Of(plan, MealIngredientRules.Entity), l => Field(l, MealIngredientRules.IngredientField) == OpValues.FormatId(pasta.Id));

        // Answered "a new one": it is.
        ImportChoices no = new();
        no.Ingredients["pappardele"] = null;
        Assert.Contains(import.Plan(no, Today, Guid.CreateVersion7).NewIngredients, i => i.Name == "Pappardele");
    }

    // A later download of the sheet: prices written in words after each name, no store named anywhere. Made up.
    private static readonly Guid Lamb = Guid.CreateVersion7(), Muffins = Guid.CreateVersion7(), Chilli = Guid.CreateVersion7(),
        Greek = Guid.CreateVersion7(), Rice = Guid.CreateVersion7();

    private static ImportKnown AfterFirstImport() => Seeds(ingredients:
    [
        new KnownIngredient(Lamb, "Lamb mince", BaseUnit.Gram, false, UnitGuessed: true),
        new KnownIngredient(Muffins, "Muffins", BaseUnit.Gram, false, UnitGuessed: true),
        new KnownIngredient(Chilli, "Sweet chilli sauce", BaseUnit.Millilitre, false, UnitGuessed: true),
        new KnownIngredient(Greek, "Greek yoghurt", BaseUnit.Gram, false, UnitGuessed: false),
        new KnownIngredient(Rice, "Rice", BaseUnit.Gram, false, UnitGuessed: true, UsualStoreId: Costco),
    ]);

    private static SheetImport Later(ImportKnown known) => new(SpreadsheetLayout.Parse([SheetGrid.FromRows("S",
        "COSTS|Meat|Pantry|Dairy|Household bits",
        "|Lamb mince $26.49 for 2kg|Sweet chili sauce $3.50 for 250ml|Natural yoghurt $4.20 for 1kg|Soap $4.50 for 100g",
        "|Muffins $2ea|Stock $9 for 12 servings|Greek yoghurt $5ea|Razors <$12 (on sale)",
        "||Rice $13 for 5kg||")], known.StoreWords), known);

    private static ImportEdit PriceOf(ImportPlan plan, Guid ingredient)
    {
        ImportEdit product = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.IngredientField) == OpValues.FormatId(ingredient));
        return Of(plan, PriceRules.Entity).Single(p => Field(p, PriceRules.ProductField) == OpValues.FormatId(product.Id));
    }

    [Fact]
    public void A_later_sheets_names_like_existing_ingredients_are_asked_and_a_likely_misspelling_suggested()
    {
        ImportReview review = Later(AfterFirstImport()).Review();

        IngredientQuestion chili = review.Ingredients.Single(q => q.Name == "Sweet chili sauce");
        Assert.Equal("sweet chilli sauce", chili.Suggested);
        Assert.Empty(chili.Meals);
        Assert.Equal(new SheetPlace("S", "C2"), chili.Place);

        IngredientQuestion natural = review.Ingredients.Single(q => q.Name == "Natural yoghurt");
        Assert.Null(natural.Suggested);
        Assert.Equal(["Greek yoghurt"], natural.Options.Select(o => o.Name));
        Assert.DoesNotContain(review.Ingredients, q => q.Name == "Stock");
    }

    [Fact]
    public void A_name_answered_as_an_existing_ingredient_prices_that_one_and_adds_none()
    {
        ImportChoices choices = new() { PriceStore = Woolworths };
        ImportPlan plan = Later(AfterFirstImport()).Plan(choices, Today, Guid.CreateVersion7);

        // Suggested and taken: the chilli sauce here gets the price; unanswered, the natural yoghurt is new.
        Assert.Equal("350", Field(PriceOf(plan, Chilli), PriceRules.PriceCentsField));
        Assert.Equal(["Natural yoghurt", "Stock"], plan.NewIngredients.Select(i => i.Name).Order());
        Assert.DoesNotContain(Of(plan, IngredientRules.Entity), e => e.Id == Chilli || e.Id == Greek || e.Id == Lamb);
    }

    [Fact]
    public void An_answer_is_remembered_by_the_product_it_priced_so_the_next_download_does_not_ask_or_add_it()
    {
        ImportChoices choices = new() { PriceStore = Woolworths };
        choices.Ingredients["natural yoghurt"] = "greek yoghurt";
        ImportPlan first = Later(AfterFirstImport()).Plan(choices, Today, Guid.CreateVersion7);

        // The product keeps the sheet's name, under the ingredient it was answered as.
        ImportEdit product = Of(first, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Natural yoghurt");
        Assert.Equal(OpValues.FormatId(Greek), Field(product, ProductRules.IngredientField));

        ImportKnown after = AfterFirstImport() with
        {
            Products = [new KnownProduct(product.Id, "Natural yoghurt", Woolworths, Greek, 1000, false)],
            Prices   = [new KnownPrice(Guid.CreateVersion7(), product.Id, 420, Today, false, PriceSource.Sheet)],
        };
        SheetImport again = Later(after);
        Assert.DoesNotContain(again.Review().Ingredients, q => q.Name == "Natural yoghurt");
        ImportPlan second = again.Plan(new ImportChoices { PriceStore = Woolworths }, Today, Guid.CreateVersion7);
        Assert.DoesNotContain(second.NewIngredients, i => i.Name == "Natural yoghurt");
        Assert.Contains("S D2 0", second.PricesAlreadyHere);
    }

    [Fact]
    public void A_price_naming_no_store_goes_to_the_usual_store_else_the_one_chosen_for_all()
    {
        ImportPlan none = Later(AfterFirstImport()).Plan(new ImportChoices(), Today, Guid.CreateVersion7);
        Assert.Contains("Lamb mince $26.49 (S B2): no store chosen.", none.LeftOut);
        // Rice's usual store is Costco: no question needed.
        ImportEdit rice = Of(none, ProductRules.Entity).Single(p => Field(p, ProductRules.IngredientField) == OpValues.FormatId(Rice));
        Assert.Equal(OpValues.FormatId(Costco), Field(rice, ProductRules.StoreField));

        ImportPlan chosen = Later(AfterFirstImport()).Plan(new ImportChoices { PriceStore = Woolworths }, Today, Guid.CreateVersion7);
        ImportEdit lamb = Of(chosen, ProductRules.Entity).Single(p => Field(p, ProductRules.IngredientField) == OpValues.FormatId(Lamb));
        Assert.Equal(OpValues.FormatId(Woolworths), Field(lamb, ProductRules.StoreField));
        Assert.Equal("2649", Field(PriceOf(chosen, Lamb), PriceRules.PriceCentsField));
    }

    [Fact]
    public void A_guessed_unit_the_price_contradicts_is_asked_and_changed_only_when_answered()
    {
        ImportChoices choices = new() { PriceStore = Woolworths };
        ImportPlan asked = Later(AfterFirstImport()).Plan(choices, Today, Guid.CreateVersion7);

        UnitQuestion muffins = asked.Units.Single();
        Assert.Equal(("muffin", BaseUnit.Gram, BaseUnit.Each), (muffins.Key, muffins.Unit, muffins.PriceSays));
        Assert.Contains(asked.LeftOut, l => l.StartsWith("Muffins $2.00 (S B3): the pack \"1 each\" - It is counted in g"));
        Assert.DoesNotContain(asked.Edits, e => e.Id == Muffins);
        Assert.Equal(0, asked.ChangedUnits);
        // Greek yoghurt's unit was confirmed by the planner: never asked, its "each" price left out with why.
        Assert.Contains(asked.LeftOut, l => l.StartsWith("Greek yoghurt $5.00 (S D3): the pack \"1 each\""));

        choices.Units["muffin"] = BaseUnit.Each;
        ImportPlan answered = Later(AfterFirstImport()).Plan(choices, Today, Guid.CreateVersion7);

        ImportEdit change = answered.Edits.Single(e => e.Id == Muffins);
        Assert.Equal(OpKind.Update, change.Kind);
        Assert.Equal([new OpField("BaseUnit", "Each"), new OpField("UnitGuessed", "false")], change.Fields);
        Assert.Equal("200", Field(PriceOf(answered, Muffins), PriceRules.PriceCentsField));
        Assert.Equal(1, answered.ChangedUnits);
    }

    [Fact]
    public void A_price_for_servings_is_left_out_saying_what_the_sheet_said_until_a_pack_is_typed()
    {
        SheetImport later = Later(AfterFirstImport());
        PriceQuestion stock = later.Review().Prices.Single(p => p.Item == "Stock");
        Assert.Equal((900, (string?)null, "12 servings"), (stock.Cents, stock.Pack, stock.Note));

        ImportChoices choices = new() { PriceStore = Woolworths };
        Assert.Contains("Stock $9.00 (S C3): how much is in the pack was not given - the sheet says \"12 servings\".",
            later.Plan(choices, Today, Guid.CreateVersion7).LeftOut);

        choices.PricePacks[stock.Id] = "1L";
        choices.Units["stock"]       = BaseUnit.Millilitre;
        ImportPlan typed = later.Plan(choices, Today, Guid.CreateVersion7);
        Assert.DoesNotContain(typed.LeftOut, l => l.StartsWith("Stock"));
    }

    [Fact]
    public void A_household_items_pack_may_be_a_weight_and_a_price_that_is_not_one_is_reported()
    {
        ImportPlan plan = Later(AfterFirstImport()).Plan(new ImportChoices { HouseholdStore = Costco }, Today, Guid.CreateVersion7);

        ImportEdit soap = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Soap");
        Assert.Equal("100", Field(soap, ProductRules.PackQuantityField));
        // Razors: added as a household product with no price; the "<$12" is reported, not read as $12.
        Assert.Single(Of(plan, ProductRules.Entity), p => Field(p, ProductRules.NameField) == "Razors");
        Assert.Contains(Later(AfterFirstImport()).Review().Unread, u => u.Text == "<$12 (on sale)" && u.Place.Address == "E3");
    }

    [Fact]
    public void Every_edit_passes_the_op_rules_the_server_applies()
    {
        ImportChoices choices = new() { HouseholdStore = Costco };
        foreach (ImportEdit edit in Plan(Seeds(), choices).Edits)
        {
            string? problem = edit.Entity switch
            {
                IngredientRules.Entity       => IngredientRules.OpProblem(edit.Kind, edit.Fields),
                MealRules.Entity             => MealRules.OpProblem(edit.Kind, edit.Fields),
                MealIngredientRules.Entity   => MealIngredientRules.OpProblem(edit.Kind, edit.Fields),
                MealGroupRules.Entity        => MealGroupRules.OpProblem(edit.Kind, edit.Fields),
                CategoryRules.Entity         => CategoryRules.OpProblem(edit.Kind, edit.Fields),
                ProductRules.Entity          => ProductRules.OpProblem(edit.Kind, edit.Fields),
                PriceRules.Entity            => PriceRules.OpProblem(edit.Kind, edit.Fields),
                _                            => $"unexpected {edit.Entity}",
            };
            Assert.Null(problem);
        }
    }
}
