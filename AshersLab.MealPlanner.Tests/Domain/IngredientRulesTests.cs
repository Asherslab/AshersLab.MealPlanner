using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class IngredientRulesTests
{
    private static readonly Guid Dairy  = new("00000000-5eed-7000-8000-000000000202");
    private static readonly Guid Costco = new("00000000-5eed-7000-8000-000000000102");

    [Theory]
    [InlineData("  beef   mince ", "beef mince")]
    [InlineData("eggs", "eggs")]
    [InlineData("\tgrated\ncheese", "grated cheese")]
    [InlineData(null, "")]
    public void Normalises_whitespace(string? input, string expected)
    {
        Assert.Equal(expected, IngredientRules.NormaliseName(input));
    }

    [Fact]
    public void A_name_is_required_and_bounded()
    {
        Assert.NotNull(IngredientRules.NameProblem(""));
        Assert.NotNull(IngredientRules.NameProblem(new string('x', IngredientRules.MaxNameLength + 1)));
        Assert.Null(IngredientRules.NameProblem(new string('x', IngredientRules.MaxNameLength)));
    }

    [Fact]
    public void Accepts_a_create_and_each_kind_of_edit()
    {
        Assert.Null(IngredientRules.OpProblem(OpKind.Create, [new("Name", "mince"), new("BaseUnit", "Gram")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Create,
        [
            new("Name", "cucumber"), new("BaseUnit", "Each"), new("CategoryId", Dairy.ToString()),
            new("UsualStoreId", Costco.ToString()), new("IsStaple", "false"),
        ]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("Name", "beef mince")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("BaseUnit", "Millilitre")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("CategoryId", null), new("UsualStoreId", null)]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("IsStaple", "true")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Create, []));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Create, [new("Name", "mince")]));        // no unit
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Create, [new("BaseUnit", "Gram")]));     // no name
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Colour", "red")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", null)]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", " untrimmed ")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", "a"), new("Name", "b")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Delete, [new("Name", "a")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("BaseUnit", null)]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("BaseUnit", "gram")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("BaseUnit", "0")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("CategoryId", "")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("CategoryId", Guid.Empty.ToString())]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("UsualStoreId", Costco.ToString("N"))]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("UsualStoreId", Costco.ToString().ToUpperInvariant())]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("IsStaple", "True")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("IsStaple", null)]));
    }

    [Fact]
    public void Shows_each_unit_as_the_planner_reads_it()
    {
        Assert.Equal("g", BaseUnit.Gram.Symbol());
        Assert.Equal("ml", BaseUnit.Millilitre.Symbol());
        Assert.Equal("each", BaseUnit.Each.Symbol());
        foreach (BaseUnit unit in Enum.GetValues<BaseUnit>())
        {
            Assert.True(BaseUnits.TryParse(BaseUnits.Format(unit), out BaseUnit back));
            Assert.Equal(unit, back);
        }
    }

    [Fact]
    public void A_new_ingredient_sends_everything_set()
    {
        IngredientDraft draft = new("cucumber", BaseUnit.Each, Dairy, null, false);

        Assert.Equal(
            [new("Name", "cucumber"), new("BaseUnit", "Each"), new("CategoryId", Dairy.ToString())],
            draft.FieldsChangedFrom(null));
    }

    [Fact]
    public void An_edit_sends_only_what_changed()
    {
        IngredientDraft before = new("cucumber", BaseUnit.Each, Dairy, Costco, false);

        Assert.Empty(before.FieldsChangedFrom(before));
        Assert.Equal([new OpField("IsStaple", "true")], (before with { IsStaple = true }).FieldsChangedFrom(before));
        Assert.Equal([new OpField("CategoryId", null)], (before with { CategoryId = null }).FieldsChangedFrom(before));
        Assert.Equal(
            [new OpField("Name", "telegraph cucumber"), new OpField("UsualStoreId", null)],
            (before with { Name = "telegraph cucumber", UsualStoreId = null }).FieldsChangedFrom(before));
    }

    [Fact]
    public void A_guessed_unit_is_sent_on_create_and_saving_it_unchanged_sends_only_that_it_was_checked()
    {
        IngredientDraft imported = new("cucumber", BaseUnit.Each, null, null, false, UnitGuessed: true);
        Assert.Contains(new OpField("UnitGuessed", "true"), imported.FieldsChangedFrom(null));
        Assert.DoesNotContain(new IngredientDraft("cucumber", BaseUnit.Each, null, null, false).FieldsChangedFrom(null), f => f.Name == "UnitGuessed");

        Assert.Equal([new OpField("UnitGuessed", "false")], (imported with { UnitGuessed = false }).FieldsChangedFrom(imported));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("UnitGuessed", "false")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("UnitGuessed", "maybe")]));
    }
}
