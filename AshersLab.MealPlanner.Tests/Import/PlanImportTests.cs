using AshersLab.MealPlanner.Shared.Domain.Import;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Import;

/// <summary>
/// The sheet's "WEEK n" plan as a plan template: how its names become meals, what it asks, its slots, and why a second
/// run imports no plan. The sheet is made up, laid out like the planner's.
/// </summary>
public class PlanImportTests
{
    private static readonly DateOnly Today = new(2026, 10, 21);

    private static readonly string[] SlotSeeds = ["Breakfast", "Snack", "Lunch", "Snack", "Dinner"];

    private static Guid SlotSeed(int index) => SheetImportTests.Seed(401 + index);

    public static SheetGrid Sheet() => SheetGrid.FromRows("Plan",
        "WEEK 1|Monday|Tuesday|Wednesday|Weekly cost",
        "Breakfast|Crumpet, poached eggs + crumpet|Muesli + yoghurt|Muesli + yoghurt|9.5",
        "Snack|Rice cakes, cheese, apple|Rice cakes, cheese, apple|Pear",
        "Lunch (packed for work)|Shepherd pie|Lmab korma|Bagles",
        "Snack|Popcorn",
        "Dinner|Leftovers/noodles/pizza|Family dinner|Takeaway, salad/soup",
        "",
        "WEEK 2|Monday",
        "Dinner|Shepherd pie",
        "",
        "MEAL IDEAS|Beef|Other",
        "|4x Shepherd pie (lamb mince, potatoes)|Frozen pizza",
        "|6x Lamb korma (lamb, korma paste)|Bagels (bagels, butter)",
        "||Muesli + yoghurt",
        "",
        "COSTS|Fruit|Pantry",
        "|Pears|Popcorn",
        "|Apples|");

    private static ImportKnown Known(IEnumerable<KnownNamed>? meals = null, IEnumerable<KnownOrdered>? slots = null, IEnumerable<KnownNamed>? templates = null) =>
        SheetImportTests.Seeds(meals: meals) with
        {
            Slots     = [.. slots ?? SlotSeeds.Select((n, i) => new KnownOrdered(SlotSeed(i), n, i + 1, false))],
            Templates = [.. templates ?? []],
        };

    private static SheetImport Import(ImportKnown known) => new(SpreadsheetLayout.Parse([Sheet()], known.StoreWords), known);

    private static ImportPlan Plan(ImportKnown known, ImportChoices? choices = null) =>
        Import(known).Plan(choices ?? new ImportChoices(), Today, Guid.CreateVersion7);

    private static string? Field(ImportEdit edit, string name) => edit.Fields.SingleOrDefault(f => f.Name == name)?.Value;

    private sealed record Planned(int Week, string Day, Guid Slot, IReadOnlyList<string> Meals);

    /// <summary>Every entry the plan writes, its choices by meal name - new meals' names from their creates, the rest from <paramref name="known"/>.</summary>
    private static List<Planned> Entries(ImportPlan plan, ImportKnown known)
    {
        Dictionary<Guid, string> names = known.Meals.ToDictionary(m => m.Id, m => m.Name);
        foreach (ImportEdit meal in plan.Edits.Where(e => e.Entity == MealRules.Entity))
            names[meal.Id] = Field(meal, MealRules.NameField)!;
        ILookup<string?, string> choices = plan.Edits.Where(e => e.Entity == TemplateChoiceRules.Entity)
            .ToLookup(c => Field(c, TemplateChoiceRules.EntryField), c => names[Guid.Parse(Field(c, TemplateChoiceRules.MealField)!)]);
        return [.. plan.Edits.Where(e => e.Entity == TemplateEntryRules.Entity).Select(e => new Planned(
            int.Parse(Field(e, TemplateEntryRules.WeekField)!), Field(e, TemplateEntryRules.DayField)!,
            Guid.Parse(Field(e, TemplateEntryRules.SlotField)!), [.. choices[OpValues.FormatId(e.Id)]]))];
    }

    private static List<List<string>> At(List<Planned> entries, int week, string day, Guid slot) =>
        [.. entries.Where(e => e.Week == week && e.Day == day && e.Slot == slot).Select(e => e.Meals.ToList())];

    [Fact]
    public void Each_cell_becomes_its_entries_named_by_as_few_meals_as_match()
    {
        ImportKnown known = Known();
        ImportPlan  plan  = Plan(known);
        List<Planned> entries = Entries(plan, known);
        Guid breakfast = SlotSeed(0), snack = SlotSeed(1), lunch = SlotSeed(2), snack2 = SlotSeed(3), dinner = SlotSeed(4);

        // Not a meal, nor its first part: its parts' items. "crumpet" twice is planned once, and said.
        Assert.Equal([["Crumpet"], ["Poached eggs"]], At(entries, 1, "Monday", breakfast));
        Assert.Contains(plan.LeftOut, l => l.Contains("\"Crumpet, poached eggs + crumpet\" names the same meal twice"));
        // A meal idea with a "+" in its name is that meal, not two.
        Assert.Equal([["Muesli + yoghurt"]], At(entries, 1, "Tuesday", breakfast));
        // Several things together: an entry each.
        Assert.Equal([["Rice cakes"], ["Cheese"], ["Apple"]], At(entries, 1, "Monday", snack));
        // The second "Snack" row is the second Snack slot.
        Assert.Equal([["Popcorn"]], At(entries, 1, "Monday", snack2));
        Assert.Equal([["Shepherd pie"]], At(entries, 1, "Monday", lunch));
        // Alternatives: one entry, a choice each, in the sheet's order.
        Assert.Equal([["Leftovers", "Noodles", "Pizza"]], At(entries, 1, "Monday", dinner));
        Assert.Equal([["Family dinner"]], At(entries, 1, "Tuesday", dinner));
        Assert.Equal([["Shepherd pie"]], At(entries, 2, "Monday", dinner));
        // A choice of several things together cannot be an entry: left out, and said where.
        Assert.Empty(At(entries, 1, "Wednesday", dinner));
        Assert.Contains("Plan D6 \"Takeaway, salad/soup\": a choice between several things eaten together - add it on the plan's page.", plan.LeftOut);
        // The weekly-cost column is not a day.
        Assert.DoesNotContain(entries, e => e.Day is not ("Monday" or "Tuesday" or "Wednesday"));
        Assert.All(plan.Edits.Where(e => e.Entity == TemplateEntryRules.Entity), e => Assert.Null(Field(e, TemplateEntryRules.PeopleField)));

        ImportEdit template = plan.Edits.Single(e => e.Entity == PlanTemplateRules.Entity);
        Assert.Equal(SheetImport.PlanTemplateId, template.Id);
        Assert.Equal("2", Field(template, PlanTemplateRules.WeekCountField));
        Assert.Equal(new PlanImportCounts(2, entries.Count, 0, 11), plan.PlanCounts);
    }

    [Fact]
    public void A_name_is_one_meal_however_often_the_plan_says_it_and_a_kind_of_meal_is_that_kind()
    {
        ImportPlan plan = Plan(Known());
        List<ImportEdit> meals = [.. plan.Edits.Where(e => e.Entity == MealRules.Entity)];

        // "Rice cakes" is in two cells; "apple" too - one meal each, reused.
        Assert.Single(meals, m => Field(m, MealRules.NameField) == "Rice cakes");
        Assert.Equal("Leftovers", Field(meals.Single(m => Field(m, MealRules.NameField) == "Leftovers"), MealRules.KindField));
        Assert.Equal("NoCost", Field(meals.Single(m => Field(m, MealRules.NameField) == "Family dinner"), MealRules.KindField));
        Assert.Equal("Simple", Field(meals.Single(m => Field(m, MealRules.NameField) == "Noodles"), MealRules.KindField));

        // A single-serve meal named as an ingredient is made of it: "Apple" of "Apples", "Pear" of "Pears".
        Guid apple = meals.Single(m => Field(m, MealRules.NameField) == "Apple").Id;
        ImportEdit line = plan.Edits.Single(e => e.Entity == MealIngredientRules.Entity && Field(e, MealIngredientRules.MealField) == OpValues.FormatId(apple));
        Assert.Equal(plan.Edits.Single(e => e.Entity == "Ingredient" && Field(e, "Name") == "Apples").Id.ToString(), Field(line, MealIngredientRules.IngredientField));
    }

    [Fact]
    public void A_meal_already_here_is_used_and_one_named_leftovers_is_not_made_again()
    {
        Guid leftovers = Guid.CreateVersion7(), korma = Guid.CreateVersion7();
        ImportKnown known = Known(meals: [new KnownNamed(leftovers, "Leftovers", false), new KnownNamed(korma, "Lamb korma", false)]);
        ImportPlan  plan  = Plan(known);

        Assert.DoesNotContain(plan.Edits, e => e.Entity == MealRules.Entity && Field(e, MealRules.NameField) is "Leftovers" or "Lamb korma");
        Assert.Contains(plan.Edits, e => e.Entity == TemplateChoiceRules.Entity && Field(e, TemplateChoiceRules.MealField) == OpValues.FormatId(leftovers));
        Assert.Contains(plan.Edits, e => e.Entity == TemplateChoiceRules.Entity && Field(e, TemplateChoiceRules.MealField) == OpValues.FormatId(korma));
    }

    [Fact]
    public void A_name_like_a_meal_is_asked_with_a_likely_one_chosen_and_nothing_like_any_is_not_asked()
    {
        ImportReview review = Import(Known()).Review();

        Assert.Equal(["bagle", "lmab korma", "pizza"], review.PlanMeals.Select(q => q.Key).Order());
        PlanMealQuestion korma = review.PlanMeals.Single(q => q.Key == "lmab korma");
        Assert.Equal("lamb korma", korma.Suggested);
        Assert.Equal([new SheetPlace("Plan", "C4")], korma.Places);
        Assert.Equal("bagel", review.PlanMeals.Single(q => q.Key == "bagle").Suggested);
        // Only possibly the same - a word in common: offered, not chosen.
        PlanMealQuestion pizza = review.PlanMeals.Single(q => q.Key == "pizza");
        Assert.Null(pizza.Suggested);
        Assert.Contains(("frozen pizza", "Frozen pizza"), pizza.Options);

        // Unanswered: the likely one is taken, the possible one is a new meal.
        ImportKnown known = Known();
        List<Planned> entries = Entries(Plan(known), known);
        Assert.Equal([["Lamb korma"]], At(entries, 1, "Tuesday", SlotSeed(2)));
        Assert.Equal([["Bagels"]], At(entries, 1, "Wednesday", SlotSeed(2)));

        // Answered: as the planner says.
        ImportChoices choices = new();
        choices.PlanMeals["lmab korma"] = null;
        choices.PlanMeals["pizza"]      = "frozen pizza";
        ImportPlan answered = Plan(known, choices);
        entries = Entries(answered, known);
        Assert.Equal([["Lmab korma"]], At(entries, 1, "Tuesday", SlotSeed(2)));
        Assert.Equal([["Leftovers", "Noodles", "Frozen pizza"]], At(entries, 1, "Monday", SlotSeed(4)));
        Assert.DoesNotContain(answered.Edits, e => e.Entity == MealRules.Entity && Field(e, MealRules.NameField) == "Pizza");
    }

    [Fact]
    public void Slots_the_day_does_not_have_are_added_once_in_the_sheets_order()
    {
        ImportKnown known = Known(slots: []);
        ImportPlan  plan  = Plan(known);

        List<ImportEdit> slots = [.. plan.Edits.Where(e => e.Entity == SlotRules.Entity)];
        Assert.Equal(["Breakfast", "Snack", "Lunch", "Snack", "Dinner"], slots.Select(s => Field(s, SlotRules.NameField)));
        Assert.Equal(["1", "2", "3", "4", "5"], slots.Select(s => Field(s, SlotRules.PositionField)));
        // Week 2's Dinner is week 1's.
        Assert.Equal(slots[4].Id, Entries(plan, known).Single(e => e.Week == 2).Slot);
        Assert.Equal(5, plan.PlanCounts.NewSlots);
        // The label's words after the slot are not lost silently.
        Assert.Contains("\"Lunch (packed for work)\" is read as the slot Lunch, for everyone - the rest of the label is not imported.", plan.LeftOut);
    }

    [Fact]
    public void A_slot_deleted_here_leaves_its_rows_out_rather_than_coming_back()
    {
        ImportKnown known = Known(slots:
        [
            .. SlotSeeds.Select((n, i) => new KnownOrdered(SlotSeed(i), n, i + 1, Deleted: i == 3)),
        ]);
        ImportPlan plan = Plan(known);

        Assert.DoesNotContain(plan.Edits, e => e.Entity == SlotRules.Entity);
        Assert.Empty(Entries(plan, known).Where(e => e.Meals.Contains("Popcorn")));
        Assert.Contains("\"Snack\": that slot was deleted here, so its row is left out of the plan.", plan.LeftOut);
    }

    [Fact]
    public void A_meal_deleted_here_is_left_out_of_the_plan_and_not_made_again()
    {
        ImportKnown known = Known(meals: [new KnownNamed(Guid.CreateVersion7(), "Noodles", true)]);
        ImportPlan  plan  = Plan(known);

        Assert.DoesNotContain(plan.Edits, e => e.Entity == MealRules.Entity && Field(e, MealRules.NameField) == "Noodles");
        Assert.Equal([["Leftovers", "Pizza"]], At(Entries(plan, known), 1, "Monday", SlotSeed(4)));
        Assert.Contains("Noodles (Plan B6): that meal was deleted here, so it is left out of the plan.", plan.LeftOut);
    }

    [Fact]
    public void The_plan_is_imported_once_whether_it_is_still_here_renamed_or_deleted()
    {
        ImportPlan first = Plan(Known());
        // What the first run wrote, as the database then holds it - the plan's meals included.
        ImportKnown after = Known(
            meals: [.. first.Edits.Where(e => e.Entity == MealRules.Entity).Select(e => new KnownNamed(e.Id, Field(e, MealRules.NameField)!, false))],
            templates: [new KnownNamed(SheetImport.PlanTemplateId, "Our rotation", false)]);

        ImportPlan again = Plan(after);
        Assert.DoesNotContain(again.Edits, e => e.Entity is PlanTemplateRules.Entity or TemplateEntryRules.Entity or TemplateChoiceRules.Entity or SlotRules.Entity);
        Assert.DoesNotContain(again.Edits, e => e.Entity == MealRules.Entity);
        Assert.Empty(Import(after).Review().PlanMeals);
        Assert.Contains("The plan: already here as \"Our rotation\", so not imported again - change it on the Plans page.", again.LeftOut);

        ImportKnown deleted = after with { Templates = [new KnownNamed(SheetImport.PlanTemplateId, "Spreadsheet plan", true)] };
        ImportPlan  gone    = Plan(deleted);
        Assert.DoesNotContain(gone.Edits, e => e.Entity is PlanTemplateRules.Entity or TemplateEntryRules.Entity or TemplateChoiceRules.Entity);
        Assert.Contains("The plan: deleted here before, so not imported again.", gone.LeftOut);
    }

    [Fact]
    public void Every_edit_the_plan_writes_passes_the_servers_rules()
    {
        ImportPlan plan = Plan(Known(slots: []));
        foreach (ImportEdit edit in plan.Edits)
            Assert.Null(Shared.Data.Sync.SyncedEntities.Find(edit.Entity)!.Problem(edit.Kind, edit.Id, edit.Fields));
    }
}
