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
            ["Beef mince", "Cheddar cheese", "Cheese", "Chicken thighs", "Chips", "Chives", "Eggs", "Grated cheese", "Gravy", "Ham",
             "Korma paste", "Lamb mince", "Pears", "Peas", "Potatoes", "Rice", "Yoghurt"],
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
        Assert.Equal("From the spreadsheet: Chips?", Field(Named(plan, MealRules.Entity, "Chips"), MealRules.NotesField));
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
        Assert.Equal(["Cheddar cheese", "Grated cheese"], cheese.Options.Select(o => o.Name).Order());
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
            "MEAL IDEAS|Snacks/desserts",
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
        choices.Ingredients["cheese"] = "grated cheese";
        choices.Serves["cottage pie"]  = 4;
        choices.Serves["lamb korma"]   = null;
        choices.MealGroups["Fruit"]    = Seed(307);
        ImportPlan plan = Plan(Seeds(), choices);

        Assert.DoesNotContain(plan.NewIngredients, i => i.Name == "Cheese");
        ImportEdit grated = Named(plan, IngredientRules.Entity, "Grated cheese");
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
        Assert.Equal("Manual", Field(price, PriceRules.SourceField));
        Assert.Equal("2026-10-21", Field(price, PriceRules.ObservedOnField));

        ImportEdit korma = Of(plan, ProductRules.Entity).Single(p => Field(p, ProductRules.NameField) == "Korma paste");
        Assert.Equal(OpValues.FormatId(Woolworths), Field(korma, ProductRules.StoreField));

        Assert.Contains("Cheddar cheese $9.50 (Plan D16): no store chosen.", plan.LeftOut);
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
        Assert.DoesNotContain(plan.LeftOut, l => l.Contains("Toilet rolls") || l.Contains("Cheddar") || l.Contains("Chicken thighs"));
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
        Assert.Contains(plan.LeftOut, l => l.StartsWith("lamb MINCE $24.99 (Plan C16): the pack \"2kg\""));
        Assert.DoesNotContain(plan.Edits, e => e.Id == lamb);
    }

    [Fact]
    public void A_price_equal_to_the_products_latest_is_not_written_again_and_a_new_one_is()
    {
        Guid lamb = Guid.CreateVersion7(), product = Guid.CreateVersion7();
        KnownProduct costcoLamb = new(product, "Lamb mince", Costco, lamb, 2000, false);
        ImportKnown same = Seeds(
            ingredients: [new KnownIngredient(lamb, "Lamb mince", BaseUnit.Gram, false)],
            products: [costcoLamb],
            prices: [new KnownPrice(Guid.CreateVersion7(), product, 2299, new DateOnly(2026, 9, 1), false),
                     new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 10, 1), false)]);
        Assert.DoesNotContain(Plan(same).Edits, e => e.Entity is ProductRules.Entity && Field(e, ProductRules.NameField) == "Lamb mince");
        Assert.DoesNotContain(Plan(same).Edits, e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));

        // The latest is $22.99 (newer day): the sheet's $24.99 is news.
        ImportKnown older = same with
        {
            Prices = [new KnownPrice(Guid.CreateVersion7(), product, 2499, new DateOnly(2026, 9, 1), false),
                      new KnownPrice(Guid.CreateVersion7(), product, 2299, new DateOnly(2026, 10, 1), false)],
        };
        Assert.Single(Plan(older).Edits, e => e.Entity is PriceRules.Entity && Field(e, PriceRules.ProductField) == OpValues.FormatId(product));
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
