using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Stores;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class NamesTests
{
    [Theory]
    [InlineData("Costco", "costco", true)]
    [InlineData(" Costco ", "COSTCO", true)]
    [InlineData("the  butcher", "The butcher", true)]
    [InlineData("Costco", "Costco Ringwood", false)]
    public void Same_ignores_case_and_spacing(string a, string b, bool same)
    {
        Assert.Equal(same, Names.Same(a, b));
    }

    [Fact]
    public void A_name_field_must_be_present_normalised_and_bounded()
    {
        Assert.Null(Names.FieldProblem("Costco", "A store"));
        Assert.Equal("A store needs a name.", Names.FieldProblem(null, "A store"));
        Assert.Equal("A store needs a name.", Names.FieldProblem("   ", "A store"));
        Assert.Equal("The name is not normalised.", Names.FieldProblem(" Costco", "A store"));
        Assert.NotNull(Names.FieldProblem(new string('x', Names.MaxLength + 1), "A store"));
    }

    [Fact]
    public void A_store_op_carries_only_a_valid_name()
    {
        Assert.Null(StoreRules.OpProblem(OpKind.Create, [new("Name", "The butcher")]));
        Assert.Null(StoreRules.OpProblem(OpKind.Update, [new("Name", "Butcher")]));
        Assert.Null(StoreRules.OpProblem(OpKind.Delete, []));

        Assert.NotNull(StoreRules.OpProblem(OpKind.Create, []));
        Assert.NotNull(StoreRules.OpProblem(OpKind.Create, [new("Position", "1")]));
        Assert.NotNull(StoreRules.OpProblem(OpKind.Update, [new("Name", "")]));
        Assert.NotNull(StoreRules.OpProblem(OpKind.Update, [new("Name", "a"), new("Name", "b")]));
        Assert.NotNull(StoreRules.OpProblem(OpKind.Delete, [new("Name", "a")]));
    }
}
