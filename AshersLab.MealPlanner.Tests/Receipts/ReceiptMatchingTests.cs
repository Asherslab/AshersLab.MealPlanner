using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>
/// Which product a receipt line is: the store's code first, then an alias, and otherwise suggestions the planner confirms -
/// never an automatic guess. And the pack size a description prints.
/// </summary>
public class ReceiptMatchingTests
{
    private static readonly Guid Beans = new("01900000-0000-7000-8000-0000000000b1");
    private static readonly Guid Bread = new("01900000-0000-7000-8000-0000000000b2");
    private static readonly Guid Mince = new("01900000-0000-7000-8000-0000000000b3");
    private static readonly Guid BeansIngredient = new("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid TomatoIngredient = new("01900000-0000-7000-8000-0000000000a2");

    private static ParsedLine Line(string description, string? code = null) =>
        new(0, description, code, null, 1, 100, 100, null, null, 0, false, false);

    private static readonly List<MatchProduct> Products =
    [
        new(Beans, "Black beans 420g", null, BeansIngredient),
        new(Bread, "Wholemeal sandwich bread 700g", null, null),
        new(Mince, "Beef mince 2kg", "195032", null),
    ];

    private static readonly List<MatchIngredient> Ingredients = [new(BeansIngredient, "Black beans"), new(TomatoIngredient, "Tomatoes")];

    [Fact]
    public void The_stores_code_matches_first_even_where_an_alias_says_otherwise()
    {
        LineMatch match = ReceiptMatching.Match(Line("CENTRAL AGRI BEEF MINCE", "195032"), Products,
            new Dictionary<string, Guid> { ["central agri beef mince"] = Bread }, Ingredients);
        Assert.Equal((Mince, MatchedBy.StoreCode), (match.ProductId, match.By));
    }

    [Fact]
    public void An_alias_matches_its_description_whatever_its_case_and_spacing()
    {
        Dictionary<string, Guid> aliases = new() { [Shared.Domain.Receipts.ProductAliasRules.Key("Woolworths black beans pdm 420g")] = Beans };
        LineMatch match = ReceiptMatching.Match(Line("WOOLWORTHS  black beans PDM 420g"), Products, aliases, Ingredients);
        Assert.Equal((Beans, MatchedBy.Alias), (match.ProductId, match.By));
        Assert.Empty(match.Suggestions);
    }

    [Fact]
    public void An_alias_to_a_product_since_deleted_matches_nothing()
    {
        Dictionary<string, Guid> aliases = new() { ["woolworths black beans pdm 420g"] = Guid.CreateVersion7() };
        LineMatch match = ReceiptMatching.Match(Line("Woolworths black beans pdm 420g"), Products, aliases, Ingredients);
        Assert.Equal((null, MatchedBy.None), (match.ProductId, match.By));
    }

    [Fact]
    public void Otherwise_it_only_suggests_the_planner_confirms_a_guess_never_prices_anything_by_itself()
    {
        LineMatch match = ReceiptMatching.Match(Line("Woolworths black beans pdm 420g"), Products, new Dictionary<string, Guid>(), Ingredients);

        Assert.Equal((null, MatchedBy.None), (match.ProductId, match.By));
        // The product whose name it shares (store brand, pack word and size ignored), and the ingredient it could be made under.
        Assert.Equal([(Beans, BeansIngredient), (null, BeansIngredient)], match.Suggestions.Select(s => (s.ProductId, s.IngredientId)));
        Assert.Equal(1.0, match.Suggestions[0].Score);
    }

    [Fact]
    public void An_ingredient_is_suggested_when_its_every_word_is_in_the_description_plurals_folded()
    {
        IReadOnlyList<MatchSuggestion> suggestions = ReceiptMatching.Suggest("Woolworths gourmet tomato punnet 1kg", [], Ingredients);
        Assert.Equal([TomatoIngredient], suggestions.Select(s => s.IngredientId));
        Assert.Empty(ReceiptMatching.Suggest("Earth choice dishwashing liquid 900ml", Products, Ingredients));
        Assert.Empty(ReceiptMatching.Suggest("700g", Products, Ingredients));
    }

    [Fact]
    public void Suggestions_are_the_best_few_in_an_order_every_device_agrees_on()
    {
        List<MatchProduct> many = [.. Enumerable.Range(0, 8).Select(i => new MatchProduct(Guid.CreateVersion7(), $"Bread loaf {(char)('h' - i)}", null, null))];
        IReadOnlyList<MatchSuggestion> one = ReceiptMatching.Suggest("Bread loaf", many, []);
        IReadOnlyList<MatchSuggestion> other = ReceiptMatching.Suggest("Bread loaf", [.. Enumerable.Reverse(many)], []);
        Assert.Equal(ReceiptMatching.MaxSuggestions, one.Count);
        Assert.Equal(one.Select(s => s.ProductId), other.Select(s => s.ProductId));
        Assert.Equal(one.OrderBy(s => s.Name).Select(s => s.Name), one.Select(s => s.Name));
    }

    [Fact]
    public void Words_drop_sizes_noise_and_fold_plurals()
    {
        Assert.Equal(new HashSet<string> { "black", "bean" }, ReceiptMatching.Words("Woolworths Black Beans pdm 420g"));
        Assert.Equal(new HashSet<string> { "berry", "tomato", "glass" }, ReceiptMatching.Words("berries tomatoes glass 2 pack"));
    }

    [Theory]
    [InlineData("Woolworths wholemeal soft sandwich bread 700g", BaseUnit.Gram, "700")]
    [InlineData("Vitasoy unsweetened oat long life milk uht 1l", BaseUnit.Millilitre, "1000")]
    [InlineData("Abe's bagels the natural pdm 4 pack", BaseUnit.Each, "4")]
    [InlineData("tomato roma red each", BaseUnit.Each, "1")]
    [InlineData("Chicken breast fillet skinless small 459g - 749g", BaseUnit.Gram, "749")]
    [InlineData("Beef mince 8x250g", BaseUnit.Gram, "2000")]
    [InlineData("Beef mince 1.4KG", BaseUnit.Gram, "1400")]
    [InlineData("Vitasoy oat milk 1l", BaseUnit.Gram, null)]
    [InlineData("tomato roma red each", BaseUnit.Gram, null)]
    [InlineData("Loose kale", BaseUnit.Gram, null)]
    public void The_pack_size_a_description_prints_in_the_ingredients_own_unit(string description, BaseUnit unit, string? expected) =>
        Assert.Equal(expected is null ? null : decimal.Parse(expected), ReceiptMatching.PackGuess(description, unit));
}

/// <summary>A receipt's purchase and a shopping-list tick for the same purchase count once; what a line puts in the pantry.</summary>
public class ReceiptStockTests
{
    private static readonly Guid Mince = new("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Bread = new("01900000-0000-7000-8000-0000000000a2");
    private static readonly DateOnly Bought = new(2026, 10, 24);

    private static TickBought Tick(int item, Guid ingredient, int daysFromReceipt) =>
        new(new Guid($"01900000-0000-7000-8000-0000000000{item:x2}"), ingredient, Bought.AddDays(daysFromReceipt), 2000);

    [Fact]
    public void A_receipt_line_replaces_the_nearest_tick_of_its_ingredient_within_the_week()
    {
        List<TickBought> ticks = [Tick(1, Mince, -6), Tick(2, Mince, -1), Tick(3, Bread, 0), Tick(4, Mince, 1)];
        // -1 and +1 are as near: the later.
        Assert.Equal(ticks[3], ReceiptStock.Covers(Mince, Bought, ticks, new HashSet<Guid>()));
        Assert.Equal(ticks[1], ReceiptStock.Covers(Mince, Bought, ticks, new HashSet<Guid> { ticks[3].ItemId }));
        Assert.Equal(ticks[0], ReceiptStock.Covers(Mince, Bought, ticks, new HashSet<Guid> { ticks[3].ItemId, ticks[1].ItemId }));
        Assert.Null(ReceiptStock.Covers(Mince, Bought, ticks, new HashSet<Guid> { ticks[0].ItemId, ticks[1].ItemId, ticks[3].ItemId }));
    }

    [Fact]
    public void A_tick_outside_the_window_is_another_purchase()
    {
        Assert.Null(ReceiptStock.Covers(Mince, Bought, [Tick(1, Mince, -(ReceiptStock.DaysBefore + 1)), Tick(2, Mince, ReceiptStock.DaysAfter + 1)], new HashSet<Guid>()));
        Assert.NotNull(ReceiptStock.Covers(Mince, Bought, [Tick(1, Mince, -ReceiptStock.DaysBefore)], new HashSet<Guid>()));
        Assert.NotNull(ReceiptStock.Covers(Mince, Bought, [Tick(1, Mince, ReceiptStock.DaysAfter)], new HashSet<Guid>()));
        Assert.Null(ReceiptStock.Covers(Bread, Bought, [Tick(1, Mince, 0)], new HashSet<Guid>()));
    }

    private static ParsedLine Line(decimal supplied, decimal? grams = null) =>
        new(0, "x", null, null, supplied, grams is null ? 100 : null, 100, grams, grams is null ? null : 1450, 0, false, false);

    [Fact]
    public void Packs_supplied_times_the_pack_or_weighed_meat_by_its_weight_and_nothing_for_nothing_supplied()
    {
        Assert.Equal(3000m, ReceiptStock.Quantity(Line(3), 1000, BaseUnit.Millilitre));
        Assert.Equal(662m, ReceiptStock.Quantity(Line(1, 662), 749, BaseUnit.Gram));
        // Weighed, but counted each: the packs.
        Assert.Equal(2m, ReceiptStock.Quantity(Line(2, 900), 1, BaseUnit.Each));
        Assert.Null(ReceiptStock.Quantity(Line(0), 1000, BaseUnit.Gram));
    }
}
