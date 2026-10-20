using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class IngredientRulesTests
{
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
    public void Accepts_a_create_and_a_rename()
    {
        Assert.Null(IngredientRules.OpProblem(OpKind.Create, [new("Name", "mince")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Update, [new("Name", "beef mince")]));
        Assert.Null(IngredientRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Create, []));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Colour", "red")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", null)]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", " untrimmed ")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Update, [new("Name", "a"), new("Name", "b")]));
        Assert.NotNull(IngredientRules.OpProblem(OpKind.Delete, [new("Name", "a")]));
    }
}
