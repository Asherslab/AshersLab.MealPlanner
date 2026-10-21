using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData("24.99", 2499)]
    [InlineData("$24.99", 2499)]
    [InlineData(" $ 24.99 ", 2499)]
    [InlineData("24.9", 2490)]
    [InlineData("24.", 2400)]
    [InlineData("24", 2400)]
    [InlineData(".99", 99)]
    [InlineData("0.05", 5)]
    [InlineData("1234.5", 123450)]
    [InlineData("100000", 10_000_000)]
    public void Reads_a_price_as_whole_cents(string typed, long cents)
    {
        Assert.Equal(cents, Money.ParseCents(typed, out string? problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("$")]
    [InlineData(".")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("-3")]
    [InlineData("2.499")]          // a third decimal place is a typo, not a fraction of a cent
    [InlineData("1,50")]           // a comma is never a decimal point
    [InlineData("1,234.50")]
    [InlineData("100000.01")]
    [InlineData("9999999999")]
    [InlineData("three dollars")]
    public void Refuses_with_a_reason(string? typed)
    {
        Assert.Null(Money.ParseCents(typed, out string? problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Theory]
    [InlineData(2499, "$24.99")]
    [InlineData(5, "$0.05")]
    [InlineData(100, "$1.00")]
    [InlineData(123456, "$1234.56")]
    [InlineData(0, "$0.00")]
    [InlineData(-150, "-$1.50")]
    public void Shows_cents_as_dollars(long cents, string expected) => Assert.Equal(expected, Money.Format(cents));

    [Theory]
    [InlineData(2499)]
    [InlineData(5)]
    [InlineData(10_000_000)]
    public void What_goes_back_in_the_box_reads_back_the_same(long cents) =>
        Assert.Equal(cents, Money.ParseCents(Money.Editable(cents), out _));
}
