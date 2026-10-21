using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Prices;

namespace AshersLab.MealPlanner.Tests.Domain;

public class IngredientPricingTests
{
    private static readonly Guid Costco     = new("00000000-5eed-7000-8000-000000000102");
    private static readonly Guid Woolworths = new("00000000-5eed-7000-8000-000000000101");
    private static readonly Guid Butcher    = new("0199f0d2-0000-7000-8000-0000000000b0");

    private static int _ids;

    /// <summary>Ids in creation order, as GUIDv7 gives them: a later reading's id sorts after an earlier one's.</summary>
    private static Guid NextId() => Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Interlocked.Increment(ref _ids)));

    private static PriceReading Seen(long cents, int day, long? perKg = null, PriceSource source = PriceSource.Manual) =>
        new(NextId(), cents, new DateOnly(2026, 10, day), source, perKg);

    private static PricedProduct Product(Guid store, decimal pack, params PriceReading[] readings) =>
        new(NextId(), store, pack, readings);

    [Fact]
    public void Nothing_priced_is_unpriced_never_zero()
    {
        Assert.Null(IngredientPricing.Price(BaseUnit.Gram, Costco, []));
        Assert.Null(IngredientPricing.Price(BaseUnit.Gram, Costco, [Product(Costco, 2000), Product(Woolworths, 500)]));
        Assert.Null(IngredientPricing.Price(BaseUnit.Gram, null, [Product(Costco, 0, Seen(2499, 14))]));
    }

    [Fact]
    public void The_usual_stores_product_wins_even_when_dearer()
    {
        PricedProduct costco     = Product(Costco, 2000, Seen(2499, 14));      // $12.50/kg
        PricedProduct woolworths = Product(Woolworths, 500, Seen(500, 14));    // $10.00/kg

        IngredientPrice price = IngredientPricing.Price(BaseUnit.Gram, Costco, [woolworths, costco])!;

        Assert.Equal(costco.ProductId, price.ProductId);
        Assert.True(price.FromUsualStore);
        Assert.Equal("$12.50/kg", price.UnitPrice.Format(BaseUnit.Gram));
    }

    [Fact]
    public void Its_latest_observation_prices_it_whatever_the_source_and_however_late_it_was_entered()
    {
        PriceReading receipt  = Seen(2299, 20, source: PriceSource.Receipt);
        PriceReading backFill = Seen(2799, 1);      // entered after the receipt, seen before it
        PricedProduct costco = Product(Costco, 2000, receipt, backFill, Seen(2599, 7));

        IngredientPrice price = IngredientPricing.Price(BaseUnit.Gram, Costco, [costco])!;

        Assert.Equal(receipt, price.Reading);
        Assert.Equal(new UnitPrice(2299, 2000), price.UnitPrice);
    }

    [Fact]
    public void With_no_priced_product_at_the_usual_store_the_cheapest_per_unit_anywhere()
    {
        PricedProduct costcoUnpriced = Product(Costco, 2000);
        PricedProduct woolworths     = Product(Woolworths, 500, Seen(650, 14));    // $13.00/kg
        PricedProduct butcher        = Product(Butcher, 1000, Seen(1200, 3));      // $12.00/kg, older but cheaper

        IngredientPrice price = IngredientPricing.Price(BaseUnit.Gram, Costco, [costcoUnpriced, woolworths, butcher])!;

        Assert.Equal(butcher.ProductId, price.ProductId);
        Assert.False(price.FromUsualStore);
    }

    [Fact]
    public void No_usual_store_means_the_cheapest_anywhere()
    {
        PricedProduct dear  = Product(Woolworths, 500, Seen(650, 14));
        PricedProduct cheap = Product(Costco, 2000, Seen(2400, 14));

        IngredientPrice price = IngredientPricing.Price(BaseUnit.Gram, null, [dear, cheap])!;

        Assert.Equal(cheap.ProductId, price.ProductId);
        Assert.False(price.FromUsualStore);
    }

    [Fact]
    public void Two_sizes_at_the_usual_store_the_cheaper_per_unit()
    {
        PricedProduct small = Product(Woolworths, 500, Seen(700, 14));      // $14.00/kg
        PricedProduct large = Product(Woolworths, 1000, Seen(1300, 2));     // $13.00/kg

        Assert.Equal(large.ProductId, IngredientPricing.Price(BaseUnit.Gram, Woolworths, [small, large])!.ProductId);
    }

    [Fact]
    public void An_equal_price_goes_to_the_newer_observation_whatever_the_order_given()
    {
        PricedProduct older = Product(Woolworths, 500, Seen(600, 2));       // $12.00/kg
        PricedProduct newer = Product(Woolworths, 1000, Seen(1200, 9));     // $12.00/kg

        Assert.Equal(newer.ProductId, IngredientPricing.Price(BaseUnit.Gram, Woolworths, [older, newer])!.ProductId);
        Assert.Equal(newer.ProductId, IngredientPricing.Price(BaseUnit.Gram, Woolworths, [newer, older])!.ProductId);
    }

    [Fact]
    public void Variable_weight_meat_is_priced_by_the_kilo_not_the_pack()
    {
        // A 1.25 kg tray at $18.73 ($14.99/kg) of a product nominally 1 kg: by the pack it would be $18.73/kg.
        PricedProduct tray = Product(Costco, 1000, Seen(1873, 14, perKg: 1499));

        IngredientPrice price = IngredientPricing.Price(BaseUnit.Gram, Costco, [tray])!;

        Assert.Equal(new UnitPrice(1499, 1000), price.UnitPrice);
        Assert.Equal(375, price.UnitPrice.CostOf(250));        // 374.75 c
    }

    [Fact]
    public void A_per_kg_price_prices_grams_and_nothing_else()
    {
        PricedProduct eggs = Product(Costco, 30, Seen(1099, 14, perKg: 999));

        Assert.Equal(new UnitPrice(1099, 30), IngredientPricing.Price(BaseUnit.Each, Costco, [eggs])!.UnitPrice);
    }

    [Theory]
    [InlineData(2499, "2000", "750", 937)]      // 937.125
    [InlineData(2499, "2000", "2000", 2499)]
    [InlineData(250, "1000", "3", 1)]           // 0.75 rounds to a cent
    [InlineData(250, "1000", "1", 0)]           // 0.25 rounds to nothing - a pinch of salt is free to the cent
    [InlineData(1875, "1000", "100", 188)]      // 187.5: half rounds away from zero
    [InlineData(1099, "30", "6", 220)]          // 219.8
    [InlineData(100, "3", "1", 33)]             // 33.33...: a ratio decimal cannot hold exactly
    [InlineData(100, "3", "3", 100)]            // ...and still exact when it comes back round
    public void A_cost_is_rounded_once_to_whole_cents(long cents, string per, string quantity, long expected)
    {
        UnitPrice price = new(cents, decimal.Parse(per, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, price.CostOf(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Prices_compare_exactly()
    {
        Assert.Equal(0, new UnitPrice(2499, 2000).CompareTo(new UnitPrice(12495, 10000)));
        Assert.True(new UnitPrice(1, 3).CompareTo(new UnitPrice(333, 1000)) > 0);    // 0.3333... > 0.333
        Assert.True(new UnitPrice(500, 500).CompareTo(new UnitPrice(1001, 1000)) < 0);
    }

    [Theory]
    [InlineData(BaseUnit.Gram, 2499, "2000", "$12.50/kg")]      // 1249.5 c rounds up
    [InlineData(BaseUnit.Millilitre, 450, "2000", "$2.25/L")]
    [InlineData(BaseUnit.Each, 1099, "30", "$0.37 each")]
    public void Shows_a_price_per_kilo_litre_or_one(BaseUnit unit, long cents, string per, string expected) =>
        Assert.Equal(expected, new UnitPrice(cents, decimal.Parse(per, System.Globalization.CultureInfo.InvariantCulture)).Format(unit));

    [Fact]
    public void A_unit_price_is_for_something()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitPrice(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitPrice(-1, 1));
    }
}
