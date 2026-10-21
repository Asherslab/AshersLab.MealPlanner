using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class ProductRulesTests
{
    private static readonly Guid Costco = new("00000000-5eed-7000-8000-000000000102");
    private static readonly Guid Mince  = new("0199f0d2-0000-7000-8000-000000000001");

    [Fact]
    public void Accepts_a_create_and_each_kind_of_edit()
    {
        Assert.Null(ProductRules.OpProblem(OpKind.Create, [new("Name", "Beef mince 8x250g"), new("StoreId", Costco.ToString()), new("PackQuantity", "2000")]));
        Assert.Null(ProductRules.OpProblem(OpKind.Create,
        [
            new("Name", "Beef mince 8x250g"), new("StoreId", Costco.ToString()), new("PackQuantity", "2000"),
            new("StoreCode", "12345"), new("IngredientId", Mince.ToString()),
        ]));
        Assert.Null(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "0.5")]));
        Assert.Null(ProductRules.OpProblem(OpKind.Update, [new("StoreCode", null), new("IngredientId", null)]));
        Assert.Null(ProductRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        Assert.NotNull(ProductRules.OpProblem(OpKind.Create, [new("Name", "Mince"), new("StoreId", Costco.ToString())]));        // no pack
        Assert.NotNull(ProductRules.OpProblem(OpKind.Create, [new("Name", "Mince"), new("PackQuantity", "2000")]));              // no store
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("StoreId", null)]));                                            // a store is never none
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "2000.0")]));                                  // not the one spelling
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "2,000")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "0")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "-1")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", "0.0001")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("PackQuantity", null)]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("StoreCode", "")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("StoreCode", " 12345")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("StoreCode", new string('1', ProductRules.MaxStoreCodeLength + 1))]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("IngredientId", "mince")]));
        Assert.NotNull(ProductRules.OpProblem(OpKind.Update, [new("Aliases", "MINCE")]));
    }

    [Theory]
    [InlineData("2000", "2000")]
    [InlineData("2000.000", "2000")]
    [InlineData("0.50", "0.5")]
    [InlineData("1234.5678", "1234.5678")]
    public void A_quantity_has_one_spelling_on_the_wire(string value, string expected)
    {
        decimal quantity = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, OpValues.FormatDecimal(quantity));
        Assert.True(OpValues.TryParseDecimal(expected, out decimal back));
        Assert.Equal(quantity, back);
    }

    [Fact]
    public void A_store_code_is_trimmed_and_none_is_null()
    {
        Assert.Equal("12345", ProductRules.NormaliseStoreCode("  12345 "));
        Assert.Null(ProductRules.NormaliseStoreCode("   "));
        Assert.Null(ProductRules.NormaliseStoreCode(null));
    }

    [Fact]
    public void A_new_product_sends_everything_set_and_an_edit_only_what_changed()
    {
        ProductDraft draft = new("Beef mince 8x250g", Costco, null, 2000m, Mince);
        Assert.Equal(["Name", "StoreId", "PackQuantity", "IngredientId"], draft.FieldsChangedFrom(null).Select(f => f.Name));

        Assert.Empty(draft.FieldsChangedFrom(draft with { PackQuantity = 2000.000m }));
        Assert.Equal([new OpField("PackQuantity", "1500")], draft with { PackQuantity = 1500 } is var smaller ? smaller.FieldsChangedFrom(draft) : null);
        Assert.Equal([new OpField("StoreCode", "12345")], (draft with { StoreCode = "12345" }).FieldsChangedFrom(draft));
        Assert.Equal([new OpField("IngredientId", null)], (draft with { IngredientId = null }).FieldsChangedFrom(draft));
    }
}
