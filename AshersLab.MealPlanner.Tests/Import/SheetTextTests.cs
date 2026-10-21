using AshersLab.MealPlanner.Shared.Domain.Import;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.Tests.Import;

/// <summary>What one cell of the planner's sheet says. Every text here is made up to match the sheet's shapes.</summary>
public class SheetTextTests
{
    private static readonly string[] Stores = ["Costco", "Woolworths", "Woolies"];

    [Fact]
    public void Reads_a_meal_with_serves_and_ingredients()
    {
        MealIdea idea = SheetText.MealIdea("4x Shepherd pie (lamb mince, potatoes, peas, gravy)");

        Assert.Equal("Shepherd pie", idea.Name);
        Assert.Equal(4, idea.Serves);
        Assert.False(idea.ServesUnsure);
        Assert.Equal(["lamb mince", "potatoes", "peas", "gravy"], idea.Ingredients!.Select(i => i.Name));
        Assert.False(idea.Unclosed);
        Assert.False(idea.Unsure);
    }

    [Theory]
    [InlineData("8?x Fish pie (fish, potato)", 8)]
    [InlineData("8x? Fish pie (fish, potato)", 8)]
    [InlineData("8 ? x Fish pie (fish, potato)", 8)]
    public void An_unsure_serves_is_read_and_flagged(string cell, int serves)
    {
        MealIdea idea = SheetText.MealIdea(cell);

        Assert.Equal("Fish pie", idea.Name);
        Assert.Equal(serves, idea.Serves);
        Assert.True(idea.ServesUnsure);
        Assert.True(idea.Unsure);
    }

    [Fact]
    public void The_serves_prefix_is_optional_and_a_name_may_start_with_a_number()
    {
        Assert.Null(SheetText.MealIdea("Cottage pie (beef mince, potato)").Serves);
        MealIdea numbered = SheetText.MealIdea("6x 3 bean chilli (beans, tomato)");
        Assert.Equal(6, numbered.Serves);
        Assert.Equal("3 bean chilli", numbered.Name);
        Assert.Null(SheetText.MealIdea("3 bean chilli (beans)").Serves);
    }

    [Fact]
    public void An_unclosed_parenthesis_takes_the_rest_of_the_cell_as_ingredients()
    {
        MealIdea idea = SheetText.MealIdea("4x Korma (chicken thighs, korma paste, rice, yoghurt");

        Assert.True(idea.Unclosed);
        Assert.Equal(["chicken thighs", "korma paste", "rice", "yoghurt"], idea.Ingredients!.Select(i => i.Name));
    }

    [Fact]
    public void A_slash_or_or_inside_an_ingredient_is_alternatives()
    {
        MealIdea idea = SheetText.MealIdea("2x Omelette (egg, cheese/ham, chives or spring onion)");

        Assert.Equal(["egg", "cheese", "chives"], idea.Ingredients!.Select(i => i.Name));
        Assert.Equal(["ham"], idea.Ingredients![1].Alternatives);
        Assert.Equal(["spring onion"], idea.Ingredients![2].Alternatives);
    }

    [Fact]
    public void A_cell_with_no_parentheses_has_no_ingredient_list_at_all()
    {
        MealIdea simple = SheetText.MealIdea("Garlic naan");
        Assert.Null(simple.Ingredients);
        Assert.Null(simple.Serves);

        Assert.Empty(SheetText.MealIdea("Toast ()").Ingredients!);
    }

    [Fact]
    public void A_question_mark_is_dropped_from_the_name_and_remembered()
    {
        MealIdea idea = SheetText.MealIdea("Chips?");

        Assert.Equal("Chips", idea.Name);
        Assert.True(idea.Unsure);
        Assert.Equal("Chips?", idea.Original);
    }

    [Fact]
    public void Text_after_the_closing_parenthesis_is_kept_as_trailing()
    {
        Assert.Equal("kids love it", SheetText.MealIdea("Tacos (beef, shells) kids love it").Trailing);
        Assert.Null(SheetText.MealIdea("Tacos (beef, shells)").Trailing);
    }

    [Fact]
    public void A_lower_case_name_is_capitalised_and_a_mixed_one_left_alone()
    {
        Assert.Equal("Fish pie", SheetText.MealIdea("fish pie").Name);
        Assert.Equal("BLT rolls", SheetText.MealIdea("BLT rolls").Name);
    }

    [Theory]
    [InlineData("Toilet rolls 14.50/24", "Toilet rolls", 1450, "24 each", null)]
    [InlineData("Lamb mince $12.50/kg", "Lamb mince", 1250, "1kg", null)]
    [InlineData("Lamb mince 24.99/2kg Costco", "Lamb mince", 2499, "2kg", "Costco")]
    [InlineData("Korma paste 4.20 (Woolies)", "Korma paste", 420, null, "Woolies")]
    [InlineData("Eggs 7.20 per 12", "Eggs", 720, "12 each", null)]
    [InlineData("Milk 2L 3.10", "Milk", 310, "2L", null)]
    [InlineData("Cola 6x375ml $9", "Cola", 900, "6x375ml", null)]
    [InlineData("Lemons 0.80 each", "Lemons", 80, "1 each", null)]
    public void Reads_a_price_written_after_a_name(string cell, string name, long cents, string? pack, string? store)
    {
        (string read, SheetPrice? price, _, _) = SheetText.CostItem(cell, Stores);

        Assert.Equal(name, read);
        Assert.Equal(new SheetPrice(cents, pack, store), price);
    }

    // The shapes of a sheet where the planner writes what each thing costs in words - every value here made up.
    [Theory]
    [InlineData("Lentils $2.40 for 375g", "Lentils", 240, "375g")]
    [InlineData("Oat milk $2.75 for 1Litre", "Oat milk", 275, "1Litre")]
    [InlineData("Frozen spinach $4.10 for kg", "Frozen spinach", 410, "1kg")]
    [InlineData("Couscous $1.70 per 500g", "Couscous", 170, "500g")]
    [InlineData("Crumpets $3.30 for 6", "Crumpets", 330, "6 each")]
    [InlineData("Napkins $5.25 for 40pk", "Napkins", 525, "40 each")]
    [InlineData("Cheese slices $6.40 for 24 slices", "Cheese slices", 640, "24 each")]
    [InlineData("Kiwifruit $0.65ea", "Kiwifruit", 65, "1 each")]
    [InlineData("Muffins $2ea", "Muffins", 200, "1 each")]
    [InlineData("Pumpkin $3.40 per pumpkin", "Pumpkin", 340, "1 each")]
    [InlineData("Cabbage $4.20 per cabbage head", "Cabbage", 420, "1 each")]
    [InlineData("Pork mince $17.49 for 12 servings (6x250g)", "Pork mince", 1749, "6x250g")]
    [InlineData("Pasta sauce $11 for 4x500g (16 servings)", "Pasta sauce", 1100, "4x500g")]
    [InlineData("Sardines $2.10 for 110g OR $0.80 for 40", "Sardines", 210, "110g")]
    [InlineData("Rolls $3.20 per bag, ~8 rolls per bag", "Rolls", 320, "1 each")]
    [InlineData("Jam (berry/apricot) ~$6 per jar (one a month)", "Jam", 600, "1 each")]
    [InlineData("Toilet rolls $14.50/24", "Toilet rolls", 1450, "24 each")]
    public void Reads_a_price_and_what_it_is_for_in_words(string cell, string name, long cents, string pack)
    {
        CostText read = SheetText.CostItem(cell, Stores);

        Assert.Equal(new CostText(name, new SheetPrice(cents, pack, null), null, null), read);
    }

    [Theory]
    [InlineData("Lasagne sheets $3 for 5 servings", "Lasagne sheets", 300, "5 servings")]
    [InlineData("Dumplings $9 for 7 (5?) servings", "Dumplings", 900, "7 servings")]
    [InlineData("Stock cubes $12 for 20 servings (3 cubes)", "Stock cubes", 1200, "20 servings")]
    [InlineData("Dish brush $14 for ~2 months", "Dish brush", 1400, "~2 months")]
    public void A_price_for_servings_or_a_time_has_no_pack_and_keeps_what_the_sheet_said(string cell, string name, long cents, string note)
    {
        Assert.Equal(new CostText(name, new SheetPrice(cents, null, null, note), null, null), SheetText.CostItem(cell, Stores));
    }

    [Theory]
    [InlineData("Relish ~ $4", "Relish", 400)]
    [InlineData("Tea $4 (try to buy on sale)", "Tea", 400)]
    [InlineData("Hot sauce $2.95", "Hot sauce", 295)]
    public void A_price_with_nothing_said_of_what_it_is_for_has_no_pack(string cell, string name, long cents)
    {
        Assert.Equal(new CostText(name, new SheetPrice(cents, null, null), null, null), SheetText.CostItem(cell, Stores));
    }

    [Theory]
    [InlineData("Razors <$12 (only on sale)", "Razors", "<$12 (only on sale)")]
    [InlineData("Sponges about $5-6", "Sponges about", "$5-6")]
    public void Something_like_a_price_that_is_not_one_is_kept_apart_from_the_name(string cell, string name, string unread)
    {
        Assert.Equal(new CostText(name, null, null, unread), SheetText.CostItem(cell, Stores));
    }

    [Theory]
    [InlineData("Eggs 12")]          // a count, not a price
    [InlineData("3 bean mix")]
    [InlineData("Peas & corn")]
    [InlineData("Wipes ")]
    [InlineData("Lamb mince 12.505")] // three decimals is a typo
    public void A_name_without_a_price_is_only_a_name(string cell)
    {
        (string name, SheetPrice? price, string? store, string? unread) = SheetText.CostItem(cell, Stores);

        Assert.Null(price);
        Assert.Null(store);
        Assert.Null(unread);
        Assert.Equal(ImportNames.Tidy(cell), name);
    }

    [Theory]
    [InlineData("Pears (Woolies)", "Woolies")]
    [InlineData("Pears - Costco", "Costco")]
    [InlineData("Pears @ costco", "Costco")]
    public void A_store_after_a_name_with_no_price_is_its_usual_store(string cell, string store)
    {
        (string name, SheetPrice? price, string? usual, _) = SheetText.CostItem(cell, Stores);

        Assert.Equal("Pears", name);
        Assert.Null(price);
        Assert.Equal(store, usual);
    }

    [Theory]
    [InlineData("17", 1700)]
    [InlineData("4.5", 450)]
    [InlineData("4.4999999999999998", 450)] // the float a number cell stores for 4.50
    [InlineData("$3", 300)]
    public void A_number_alone_in_a_price_cell_is_a_price(string cell, long cents)
    {
        Assert.Equal(cents, SheetText.Price(cell, Stores, alone: true)?.Cents);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("4.505")]
    [InlineData("100001")]
    [InlineData("4.50/")]
    public void Not_a_price(string cell)
    {
        Assert.Null(SheetText.Price(cell, Stores, alone: true));
    }

    [Fact]
    public void A_plan_cell_splits_choices_on_slashes_and_items_on_commas_and_pluses()
    {
        PlanCell cell = SheetText.PlanCell("Leftovers/noodles/pizza");
        Assert.Equal([["Leftovers"], ["noodles"], ["pizza"]], cell.Choices);

        Assert.Equal([["Toast", "jam", "fruit"]], SheetText.PlanCell("Toast, jam + fruit").Choices);
        Assert.Equal([["Fish and chips"]], SheetText.PlanCell("Fish and chips").Choices);
        Assert.Equal([["Schnitzel", "peas and corn"]], SheetText.PlanCell("Schnitzel, peas and corn").Choices);
    }

    [Theory]
    [InlineData("Lunch (at work)", "Lunch")]
    [InlineData("  Dinner ", "Dinner")]
    [InlineData("Snack", "Snack")]
    public void A_slot_is_the_leading_word_of_its_label(string label, string slot)
    {
        Assert.Equal(slot, SheetText.SlotName(label));
    }
}

public class ImportNamesTests
{
    [Theory]
    [InlineData("bananas", "Bananas")]
    [InlineData("egg", "Eggs")]
    [InlineData("chicken schnitzel ", "Chicken schnitzel")]
    [InlineData("Chips?", "chips")]
    [InlineData("tortilla", "Tortillas")]
    [InlineData("Yoghurt pouch", "yoghurt pouches")]
    [InlineData("potato", "Potatoes")]
    [InlineData("Berry", "berries")]
    [InlineData("Chicken & egg", "chicken/egg")]
    public void The_same_name_written_differently_has_one_key(string a, string b)
    {
        Assert.Equal(ImportNames.Key(a), ImportNames.Key(b));
    }

    [Theory]
    [InlineData("peas", "Peas & corn")]
    [InlineData("cheese", "Grated cheese")]
    [InlineData("glass", "glas")]
    public void Different_names_have_different_keys(string a, string b)
    {
        Assert.NotEqual(ImportNames.Key(a), ImportNames.Key(b));
    }

    [Fact]
    public void A_swapped_pair_of_letters_is_one_edit()
    {
        Assert.Equal(1, ImportNames.Distance("lmab", "lamb"));
        Assert.Equal(2, ImportNames.Distance("abcd", "badc"));
        Assert.Equal(3, ImportNames.Distance("kitten", "sitting"));
    }

    private static readonly string[] Known = ["Lamb korma", "Grated cheese", "Sliced cheese", "Cheese cubes", "Roast potatoes", "Rice"];

    [Fact]
    public void A_letter_transposition_is_likely_the_same()
    {
        NameCandidate<string> best = ImportNames.Candidates("Lmab korma", Known, k => k).First();

        Assert.Equal("Lamb korma", best.Item);
        Assert.Equal(MatchStrength.Likely, best.Strength);
    }

    [Fact]
    public void One_word_of_several_is_only_possible_and_every_such_name_is_offered()
    {
        List<NameCandidate<string>> found = ImportNames.Candidates("cheese", Known, k => k);

        Assert.Equal(["Cheese cubes", "Grated cheese", "Sliced cheese"], found.Select(c => c.Item).Order());
        Assert.All(found, c => Assert.Equal(MatchStrength.Possible, c.Strength));
    }

    [Fact]
    public void A_name_wholly_inside_comes_before_ones_only_sharing_a_word_or_spelt_alike()
    {
        string[] known = ["Frozen kale", "Frozen pies", "Frozen naan", "Frozen dough", "Frozen limes", "Beans & rice"];

        List<NameCandidate<string>> found = ImportNames.Candidates("Frozen beans & rice", known, k => k);

        Assert.Equal("Beans & rice", found[0].Item);
        Assert.True(found[0].Inside);
        Assert.Equal(5, found.Count);
    }

    [Fact]
    public void A_shared_long_word_is_possible_and_nothing_in_common_is_none()
    {
        Assert.Equal("Roast potatoes", ImportNames.Candidates("roast pumpkin", Known, k => k).Single().Item);
        Assert.Empty(ImportNames.Candidates("gravy", Known, k => k));
    }

    [Fact]
    public void Short_names_one_letter_apart_are_not_likely()
    {
        // "rice" and "mice" are not a typo of each other.
        Assert.DoesNotContain(ImportNames.Candidates("mice", Known, k => k), c => c.Strength >= MatchStrength.Likely);
    }

    [Fact]
    public void Equal_keys_are_the_same()
    {
        Assert.Equal(MatchStrength.Same, ImportNames.Candidates("rices", Known, k => k).Single().Strength);
    }
}

public class UnitGuessTests
{
    [Theory]
    // The scope's own examples.
    [InlineData("Bagels", BaseUnit.Each)]
    [InlineData("Eggs", BaseUnit.Each)]
    [InlineData("Wrap", BaseUnit.Each)]
    [InlineData("Tortillas", BaseUnit.Each)]
    [InlineData("Avocado", BaseUnit.Each)]
    [InlineData("bananas", BaseUnit.Each)]
    [InlineData("Lamb mince", BaseUnit.Gram)]
    [InlineData("Cheddar cheese", BaseUnit.Gram)]
    [InlineData("Rice", BaseUnit.Gram)]
    [InlineData("Pasta", BaseUnit.Gram)]
    [InlineData("Soy sauce", BaseUnit.Millilitre)]
    [InlineData("Milk", BaseUnit.Millilitre)]
    [InlineData("Olive oil", BaseUnit.Millilitre)]
    // The more specific words win.
    [InlineData("Lime juice", BaseUnit.Millilitre)]
    [InlineData("Lime", BaseUnit.Each)]
    [InlineData("Tinned tomato", BaseUnit.Gram)]
    [InlineData("Tomato", BaseUnit.Each)]
    [InlineData("Diced onions", BaseUnit.Gram)]
    [InlineData("Red onion", BaseUnit.Each)]
    [InlineData("Garlic bread", BaseUnit.Each)]
    [InlineData("Minced garlic", BaseUnit.Gram)]
    [InlineData("Frozen pizza", BaseUnit.Each)]
    [InlineData("Frozen peas", BaseUnit.Gram)]
    [InlineData("Yoghurt pouches", BaseUnit.Each)]
    [InlineData("Yoghurt", BaseUnit.Gram)]
    public void Guesses_by_the_words_of_the_name(string name, BaseUnit unit)
    {
        Assert.Equal(unit, UnitGuess.For(name, null));
    }

    [Theory]
    [InlineData("Kohlrabi", "Vegetables", BaseUnit.Each)]
    [InlineData("Quark", "Dairy", BaseUnit.Gram)]
    [InlineData("Kombucha", "Fridge", BaseUnit.Millilitre)]
    [InlineData("Kombucha", null, BaseUnit.Gram)]
    [InlineData("Kombucha", "Odds and ends", BaseUnit.Gram)]
    public void Falls_back_to_the_category_then_grams(string name, string? category, BaseUnit unit)
    {
        Assert.Equal(unit, UnitGuess.For(name, category));
    }

    [Theory]
    [InlineData("24", BaseUnit.Each)]
    [InlineData("2kg", BaseUnit.Gram)]
    [InlineData("6x375ml", BaseUnit.Millilitre)]
    [InlineData("18pk", BaseUnit.Each)]
    public void A_pack_the_sheet_gave_beats_the_name(string pack, BaseUnit unit)
    {
        // "Mince" alone would be grams; a pack says what it is counted in.
        Assert.Equal(unit, SheetImport.GuessUnit("Mince", null, [null, pack]));
    }
}
