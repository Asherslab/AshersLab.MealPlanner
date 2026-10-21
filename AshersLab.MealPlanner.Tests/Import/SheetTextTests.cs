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
    [InlineData("Toilet rolls 14.50/24", "Toilet rolls", 1450, "24", null)]
    [InlineData("Lamb mince $12.50/kg", "Lamb mince", 1250, "1kg", null)]
    [InlineData("Lamb mince 24.99/2kg Costco", "Lamb mince", 2499, "2kg", "Costco")]
    [InlineData("Korma paste 4.20 (Woolies)", "Korma paste", 420, null, "Woolies")]
    [InlineData("Eggs 7.20 per 12", "Eggs", 720, "12", null)]
    [InlineData("Milk 2L 3.10", "Milk", 310, "2L", null)]
    [InlineData("Cola 6x375ml $9", "Cola", 900, "6x375ml", null)]
    [InlineData("Lemons 0.80 each", "Lemons", 80, "1", null)]
    public void Reads_a_price_written_after_a_name(string cell, string name, long cents, string? pack, string? store)
    {
        (string read, SheetPrice? price, _) = SheetText.CostItem(cell, Stores);

        Assert.Equal(name, read);
        Assert.Equal(new SheetPrice(cents, pack, store), price);
    }

    [Theory]
    [InlineData("Eggs 12")]          // a count, not a price
    [InlineData("3 bean mix")]
    [InlineData("Peas & corn")]
    [InlineData("Wipes ")]
    [InlineData("Lamb mince 12.505")] // three decimals is a typo
    public void A_name_without_a_price_is_only_a_name(string cell)
    {
        (string name, SheetPrice? price, string? store) = SheetText.CostItem(cell, Stores);

        Assert.Null(price);
        Assert.Null(store);
        Assert.Equal(ImportNames.Tidy(cell), name);
    }

    [Theory]
    [InlineData("Pears (Woolies)", "Woolies")]
    [InlineData("Pears - Costco", "Costco")]
    [InlineData("Pears @ costco", "Costco")]
    public void A_store_after_a_name_with_no_price_is_its_usual_store(string cell, string store)
    {
        (string name, SheetPrice? price, string? usual) = SheetText.CostItem(cell, Stores);

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

    private static readonly string[] Known = ["Lamb korma", "Grated cheese", "Sliced cheese", "Cheese cubes", "Roast chook", "Rice"];

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
    public void A_shared_long_word_is_possible_and_nothing_in_common_is_none()
    {
        Assert.Equal("Roast chook", ImportNames.Candidates("roast chicken", Known, k => k).Single().Item);
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
