using AshersLab.MealPlanner.Shared.Domain.Shopping;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>Shopping lists and their items as the op log sees them: derived ids, what a create carries, each field's spelling.</summary>
public class ShoppingRulesTests
{
    private static readonly DateOnly Mon = new(2026, 10, 26);
    private static readonly Guid Mince = new("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Costco = new("01900000-0000-7000-8000-0000000000c1");

    [Fact]
    public void A_list_id_is_the_same_on_every_device_for_the_same_days_and_differs_for_others()
    {
        Guid list = ShoppingListRules.ListId(Mon, Mon.AddDays(6));
        Assert.Equal(list, ShoppingListRules.ListId(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 1)));
        Assert.NotEqual(list, ShoppingListRules.ListId(Mon, Mon.AddDays(5)));
        Assert.NotEqual(list, ShoppingListRules.ListId(Mon.AddDays(1), Mon.AddDays(6)));
        Assert.Equal(8, list.Version);

        Guid tick = ShoppingItemRules.TickId(list, Mince);
        Assert.Equal(tick, ShoppingItemRules.TickId(list, Mince));
        Assert.NotEqual(tick, ShoppingItemRules.TickId(ShoppingListRules.ListId(Mon, Mon.AddDays(5)), Mince));
        Assert.NotEqual(tick, list);
    }

    [Fact]
    public void A_list_carries_its_two_dates_in_order_and_no_longer_than_five_weeks()
    {
        Assert.Null(ShoppingListRules.OpProblem(OpKind.Create, ShoppingListRules.CreateFields(Mon, Mon.AddDays(6))));
        Assert.Equal(new[] { "2026-10-26", "2026-11-01" }, ShoppingListRules.CreateFields(Mon, Mon.AddDays(6)).Select(f => f.Value));
        Assert.Null(ShoppingListRules.OpProblem(OpKind.Create, ShoppingListRules.CreateFields(Mon, Mon)));
        Assert.Null(ShoppingListRules.OpProblem(OpKind.Create, ShoppingListRules.CreateFields(Mon, Mon.AddDays(34))));
        Assert.NotNull(ShoppingListRules.OpProblem(OpKind.Create, ShoppingListRules.CreateFields(Mon, Mon.AddDays(35))));
        Assert.NotNull(ShoppingListRules.OpProblem(OpKind.Create, ShoppingListRules.CreateFields(Mon, Mon.AddDays(-1))));
        Assert.NotNull(ShoppingListRules.OpProblem(OpKind.Create, [new OpField(ShoppingListRules.StartsOnField, "2026-10-26")]));
        Assert.NotNull(ShoppingListRules.OpProblem(OpKind.Create, [new OpField(ShoppingListRules.StartsOnField, "26/10/2026"), new OpField(ShoppingListRules.EndsOnField, "2026-11-01")]));
    }

    [Fact]
    public void A_tick_names_its_list_and_ingredient_and_says_how_it_is_ticked()
    {
        Guid list = ShoppingListRules.ListId(Mon, Mon.AddDays(6));
        IReadOnlyList<OpField> fields = ShoppingItemRules.TickFields(list, Mince, ShoppingTick.Bought);
        Assert.Equal(new[] { "ListId", "IngredientId", "Tick" }, fields.Select(f => f.Name));
        Assert.Equal("Bought", fields[2].Value);
        Assert.Null(ShoppingItemRules.OpProblem(OpKind.Create, fields));
        Assert.Null(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.TickField, "HaveIt")]));
        Assert.Null(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.TickField, "None")]));
        // One spelling: the member's name.
        foreach (string wrong in new[] { "bought", "1", "Have it", "" })
            Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.TickField, wrong)]));
        Assert.True(ShoppingTicks.TryParse("HaveIt", out ShoppingTick haveIt) && haveIt == ShoppingTick.HaveIt);
    }

    [Fact]
    public void A_new_item_is_a_tick_or_an_extra_never_both_nor_neither()
    {
        Guid list = ShoppingListRules.ListId(Mon, Mon.AddDays(6));
        OpField onList = new(ShoppingItemRules.ListField, OpValues.FormatId(list));
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Create, [onList]));
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Create, [onList, new(ShoppingItemRules.IngredientField, OpValues.FormatId(Mince)), new(ShoppingItemRules.NameField, "Bin bags")]));
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Create, [new(ShoppingItemRules.NameField, "Bin bags")]));
    }

    [Fact]
    public void An_extra_item_has_a_normalised_name_and_maybe_a_store_and_a_price()
    {
        Guid list = ShoppingListRules.ListId(Mon, Mon.AddDays(6));
        IReadOnlyList<OpField> bags = new ExtraItemDraft("  Bin   bags ", Costco, 1299).FieldsChangedFrom(list, null);
        Assert.Equal(new[] { "ListId", "Name", "StoreId", "PriceCents" }, bags.Select(f => f.Name));
        Assert.Equal("Bin bags", bags[1].Value);
        Assert.Equal("1299", bags[3].Value);
        Assert.Null(ShoppingItemRules.OpProblem(OpKind.Create, bags));

        // Nothing but a name.
        Assert.Equal(new[] { "ListId", "Name" }, new ExtraItemDraft("Candles", null, null).FieldsChangedFrom(list, null).Select(f => f.Name));
        // An edit sends only what changed; taking the price off sends it as null.
        ExtraItemDraft was = new("Bin bags", Costco, 1299);
        Assert.Empty(new ExtraItemDraft("Bin bags", Costco, 1299).FieldsChangedFrom(list, was));
        IReadOnlyList<OpField> noPrice = new ExtraItemDraft("Bin bags", Costco, null).FieldsChangedFrom(list, was);
        Assert.Equal((ShoppingItemRules.PriceCentsField, (string?)null), (noPrice.Single().Name, noPrice.Single().Value));
        Assert.Null(ShoppingItemRules.OpProblem(OpKind.Update, noPrice));

        Assert.NotNull(new ExtraItemDraft("  ", null, null).Problem());
        Assert.NotNull(new ExtraItemDraft("Bags", null, 0).Problem());
        Assert.Null(new ExtraItemDraft("Bags", null, 1).Problem());
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.NameField, " Bags")]));
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.PriceCentsField, "12.99")]));
        Assert.NotNull(ShoppingItemRules.OpProblem(OpKind.Update, [new OpField(ShoppingItemRules.PriceCentsField, "0")]));
    }
}
