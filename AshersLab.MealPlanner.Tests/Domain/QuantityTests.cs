using System.Globalization;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Products;

namespace AshersLab.MealPlanner.Tests.Domain;

public class QuantityTests
{
    private static readonly Guid Meal  = new("0199f0d2-0000-7000-8000-00000000000a");
    private static readonly Guid Mince = new("0199f0d2-0000-7000-8000-000000000001");

    // Each has more digits than Format shows ("1.235 kg"): the box must hold all of them.
    [Theory]
    [InlineData("1234.5", BaseUnit.Gram, "1.2345kg")]
    [InlineData("1234.567", BaseUnit.Gram, "1.234567kg")]
    [InlineData("999999.999", BaseUnit.Gram, "999.999999kg")]
    [InlineData("1000.001", BaseUnit.Millilitre, "1.000001L")]
    [InlineData("2000", BaseUnit.Gram, "2kg")]
    [InlineData("999.999", BaseUnit.Gram, "999.999g")]
    [InlineData("0.001", BaseUnit.Each, "0.001each")]
    public void The_edit_box_holds_every_digit_and_reads_back_exactly(string stored, BaseUnit unit, string box)
    {
        decimal quantity = decimal.Parse(stored, CultureInfo.InvariantCulture);

        Assert.Equal(box, Quantity.Editable(quantity, unit));
        Assert.Equal(quantity, MealIngredientRules.ParseQuantity(Quantity.Editable(quantity, unit), unit, out string? problem));
        Assert.Null(problem);
        Assert.Equal(quantity, PackSize.Parse(PackSize.Editable(quantity, unit), unit, out problem));
        Assert.Null(problem);
    }

    [Fact]
    public void Saving_the_sheet_without_touching_the_quantity_sends_no_op()
    {
        // What MealIngredientSheet does: the box from Editable, read again on Save, compared with the row.
        MealIngredientDraft row = new(Meal, Mince, 1234.5m, null);
        decimal? saved = MealIngredientRules.ParseQuantity(Quantity.Editable(row.Quantity!.Value, BaseUnit.Gram), BaseUnit.Gram, out _);

        Assert.Empty((row with { Quantity = saved }).FieldsChangedFrom(row));
    }

    [Theory]
    [InlineData("99999999999999999999999999999999")]              // more digits than a decimal holds
    [InlineData("99999999999999999999999999999999kg")]
    [InlineData("99999999999999999999999999999999x1g")]           // the count too big
    [InlineData("79228162514264337593543950335x2")]               // each fits, the product does not
    [InlineData("79228162514264337593543950335kg")]               // fits, until it is made grams
    public void A_number_too_big_for_a_decimal_is_refused_not_thrown(string typed)
    {
        Assert.Null(Quantity.Read(typed, BaseUnit.Gram, out string? problem));
        Assert.Equal("That number is too big.", problem);
        Assert.Null(MealIngredientRules.ParseQuantity(typed, BaseUnit.Gram, out problem));
        Assert.NotNull(problem);
        Assert.Null(PackSize.Parse(typed, BaseUnit.Gram, out problem));
        Assert.NotNull(problem);
    }
}
