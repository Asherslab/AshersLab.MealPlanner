using AshersLab.MealPlanner.Shared.Data.Receipts;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Receipts;

namespace AshersLab.MealPlanner.Tests.Receipts;

/// <summary>What the review page decides from a line's choice, its new product's fields and the tick's checkbox.</summary>
public class ReviewChoiceTests
{
    private static readonly Guid Product = Guid.CreateVersion7(), Ingredient = Guid.CreateVersion7(), Tick = Guid.CreateVersion7();

    [Fact]
    public void The_tick_is_replaced_only_while_its_box_is_ticked()
    {
        Assert.Equal(Tick, ReviewChoice.Decide($"p:{Product}", null, null, BaseUnit.Gram, Tick, cover: true)!.Covers);
        Assert.Null(ReviewChoice.Decide($"p:{Product}", null, null, BaseUnit.Gram, Tick, cover: false)!.Covers);
        Assert.Equal(Tick, ReviewChoice.Decide($"n:{Ingredient}", "Beans 420g", "420g", BaseUnit.Gram, Tick, cover: true)!.Covers);
        Assert.Null(ReviewChoice.Decide($"n:{Ingredient}", "Beans 420g", "420g", BaseUnit.Gram, Tick, cover: false)!.Covers);
    }

    [Fact]
    public void A_choice_survives_a_correction_only_while_the_line_is_the_same_item()
    {
        ParsedLine read = new(1, "PINK LADY APPLES 2KG", "700107", null, 1, 899, 839, null, null, 0, false, false);
        Assert.True(ReviewChoice.Survives(read, read with { AmountCents = 899 }));
        // The item number corrected: another product, matched afresh - not imported under the misread's choice.
        Assert.False(ReviewChoice.Survives(read, read with { StoreCode = "700101" }));
        Assert.False(ReviewChoice.Survives(read, read with { Description = "PINK LADY APPLES 1KG" }));
    }

    [Fact]
    public void Each_choice_decides_its_line()
    {
        Assert.Equal(new LineDecision(Product, null, false, null), ReviewChoice.Decide($"p:{Product}", null, null, BaseUnit.Gram, null, true));
        Assert.Equal(new LineDecision(null, new NewProduct("Beans 420g", Ingredient, 420), false, null),
            ReviewChoice.Decide($"n:{Ingredient}", " Beans  420g ", "420g", BaseUnit.Gram, null, true));
        Assert.Equal(new LineDecision(null, new NewProduct("Cleaner", null, 1), false, null), ReviewChoice.Decide("h", "Cleaner", "1", BaseUnit.Each, null, true));
        // Left out: never replaces a tick, whatever the box says.
        Assert.Equal(new LineDecision(null, null, true, null), ReviewChoice.Decide("x", null, null, BaseUnit.Gram, Tick, true));
        Assert.Equal(new LineDecision(null, null, false, null), ReviewChoice.Decide("", null, null, BaseUnit.Gram, Tick, true));
        // A new product without a name or a pack is not a decision yet.
        Assert.Null(ReviewChoice.Decide($"n:{Ingredient}", "", "420g", BaseUnit.Gram, null, true));
        Assert.Null(ReviewChoice.Decide($"n:{Ingredient}", "Beans", "lots", BaseUnit.Gram, null, true));
    }
}
