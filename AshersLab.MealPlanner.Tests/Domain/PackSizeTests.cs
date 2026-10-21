using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Products;

namespace AshersLab.MealPlanner.Tests.Domain;

public class PackSizeTests
{
    [Theory]
    [InlineData("2000", BaseUnit.Gram, "2000")]
    [InlineData("500g", BaseUnit.Gram, "500")]
    [InlineData(" 500 G ", BaseUnit.Gram, "500")]
    [InlineData("2kg", BaseUnit.Gram, "2000")]
    [InlineData("1.4KG", BaseUnit.Gram, "1400")]
    [InlineData("8x250g", BaseUnit.Gram, "2000")]
    [InlineData("8 × 250 g", BaseUnit.Gram, "2000")]
    [InlineData("3*1.5kg", BaseUnit.Gram, "4500")]
    [InlineData(".5kg", BaseUnit.Gram, "500")]
    [InlineData("0.5", BaseUnit.Gram, "0.5")]
    [InlineData("375ml", BaseUnit.Millilitre, "375")]
    [InlineData("2L", BaseUnit.Millilitre, "2000")]
    [InlineData("1.25 litres", BaseUnit.Millilitre, "1250")]
    [InlineData("6x375ml", BaseUnit.Millilitre, "2250")]
    [InlineData("12", BaseUnit.Each, "12")]
    [InlineData("18pk", BaseUnit.Each, "18")]
    [InlineData("148CT", BaseUnit.Each, "148")]
    [InlineData("6x2", BaseUnit.Each, "12")]
    public void Reads_what_the_pack_says_in_the_ingredients_unit(string typed, BaseUnit unit, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), PackSize.Parse(typed, unit, out string? problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("", BaseUnit.Gram)]
    [InlineData(null, BaseUnit.Gram)]
    [InlineData("500ml", BaseUnit.Gram)]       // no conversion between grams and millilitres
    [InlineData("2kg", BaseUnit.Millilitre)]
    [InlineData("2kg", BaseUnit.Each)]
    [InlineData("0", BaseUnit.Gram)]
    [InlineData("0x500g", BaseUnit.Gram)]
    [InlineData("-5", BaseUnit.Gram)]
    [InlineData("1,5kg", BaseUnit.Gram)]       // a comma is never read as a decimal point
    [InlineData("two kilos", BaseUnit.Gram)]
    [InlineData("1001kg", BaseUnit.Gram)]      // more than a tonne: a typo
    [InlineData("0.0001", BaseUnit.Gram)]      // past three decimal places
    public void Refuses_with_a_reason(string? typed, BaseUnit unit)
    {
        Assert.Null(PackSize.Parse(typed, unit, out string? problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Theory]
    [InlineData("2000", BaseUnit.Gram, "2 kg")]
    [InlineData("1400", BaseUnit.Gram, "1.4 kg")]
    [InlineData("500", BaseUnit.Gram, "500 g")]
    [InlineData("999.5", BaseUnit.Gram, "999.5 g")]
    [InlineData("1250", BaseUnit.Millilitre, "1.25 L")]
    [InlineData("375", BaseUnit.Millilitre, "375 ml")]
    [InlineData("12", BaseUnit.Each, "12 each")]
    [InlineData("2000.000", BaseUnit.Gram, "2 kg")]
    public void Shows_it_as_a_pack_would(string quantity, BaseUnit unit, string expected)
    {
        Assert.Equal(expected, PackSize.Format(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), unit));
    }

    [Theory]
    [InlineData("1400", BaseUnit.Gram)]
    [InlineData("0.5", BaseUnit.Gram)]
    [InlineData("2250", BaseUnit.Millilitre)]
    [InlineData("18", BaseUnit.Each)]
    public void What_goes_back_in_the_box_reads_back_the_same(string quantity, BaseUnit unit)
    {
        decimal value = decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(value, PackSize.Parse(PackSize.Editable(value, unit), unit, out _));
    }
}
