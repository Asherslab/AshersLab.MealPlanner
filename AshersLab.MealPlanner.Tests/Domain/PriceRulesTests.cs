using AshersLab.MealPlanner.Shared.Domain.Prices;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class PriceRulesTests
{
    private static readonly Guid Product = new("0199f0d2-0000-7000-8000-000000000001");

    [Fact]
    public void Accepts_a_create_a_delete_and_an_update()
    {
        Assert.Null(PriceRules.OpProblem(OpKind.Create, new PriceEntry(Product, 2499, new DateOnly(2026, 10, 21), PriceSource.Manual, null).Fields()));
        Assert.Null(PriceRules.OpProblem(OpKind.Create, new PriceEntry(Product, 1873, new DateOnly(2026, 10, 21), PriceSource.Receipt, 1499).Fields()));
        Assert.Null(PriceRules.OpProblem(OpKind.Delete, []));
        // The pages never edit a price, but a browser giving back what a restored server lost sends updates.
        Assert.Null(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "2599"), new("PerKgCents", null)]));
    }

    [Fact]
    public void A_new_price_carries_exactly_its_fields()
    {
        Assert.Equal(
            [
                new OpField("ProductId", Product.ToString()), new OpField("PriceCents", "2499"),
                new OpField("ObservedOn", "2026-10-21"), new OpField("Source", "Manual"),
            ],
            new PriceEntry(Product, 2499, new DateOnly(2026, 10, 21), PriceSource.Manual, null).Fields());
        Assert.Contains(new OpField("PerKgCents", "1499"),
            new PriceEntry(Product, 1873, new DateOnly(2026, 10, 21), PriceSource.Manual, 1499).Fields());
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        List<OpField> valid = new PriceEntry(Product, 2499, new DateOnly(2026, 10, 21), PriceSource.Manual, null).Fields();
        foreach (string missing in new[] { "ProductId", "PriceCents", "ObservedOn", "Source" })
            Assert.NotNull(PriceRules.OpProblem(OpKind.Create, [.. valid.Where(f => f.Name != missing)]));

        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("ProductId", null)]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "24.99")]));     // cents, never dollars
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "02499")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "0")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "-1")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", "10000001")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PriceCents", null)]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("ObservedOn", "21/10/2026")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("ObservedOn", "2026-10-21T00:00:00")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("Source", "manual")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("Source", "0")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PerKgCents", "0")]));
        Assert.NotNull(PriceRules.OpProblem(OpKind.Update, [new("PerKgCents", "")]));
    }

    [Fact]
    public void The_latest_is_the_newest_day_then_the_last_entered_whatever_the_source()
    {
        PriceReading older    = new(Guid.CreateVersion7(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)), 2600, new DateOnly(2026, 10, 1), PriceSource.Manual, null);
        PriceReading first    = new(Guid.CreateVersion7(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero)), 2499, new DateOnly(2026, 10, 14), PriceSource.Receipt, null);
        PriceReading sameDay  = new(Guid.CreateVersion7(new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero)), 2299, new DateOnly(2026, 10, 14), PriceSource.Manual, null);

        // Entered later but seen earlier: a back-filled price does not replace a newer one.
        Assert.Equal(sameDay, PriceReadings.Latest([older, first, sameDay]));
        Assert.Equal(sameDay, PriceReadings.Latest([sameDay, first, older]));
        Assert.Equal([sameDay, first, older], PriceReadings.NewestFirst([first, older, sameDay]));
        Assert.Null(PriceReadings.Latest([]));
    }
}
