using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class MealRulesTests
{
    private static readonly Guid Beef = new("00000000-5eed-7000-8000-000000000301");

    [Fact]
    public void Accepts_a_create_and_each_kind_of_edit()
    {
        Assert.Null(MealRules.OpProblem(OpKind.Create, [new("Name", "Spaghetti bolognese"), new("Kind", "Cooked"), new("ServesPerBatch", "4")]));
        Assert.Null(MealRules.OpProblem(OpKind.Create,
        [
            new("Name", "Pizza"), new("Kind", "FixedCost"), new("ServesPerBatch", "3"), new("FixedCostCents", "4500"),
            new("GroupId", Beef.ToString()), new("Notes", "Friday.\nFrom the place on the corner."),
        ]));
        foreach (MealKind kind in Enum.GetValues<MealKind>())
            Assert.Null(MealRules.OpProblem(OpKind.Update, [new("Kind", kind.ToString())]));
        Assert.Null(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", "99")]));
        Assert.Null(MealRules.OpProblem(OpKind.Update, [new("GroupId", null), new("Notes", null), new("FixedCostCents", null)]));
        Assert.Null(MealRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        Assert.NotNull(MealRules.OpProblem(OpKind.Create, [new("Name", "Risoni"), new("Kind", "Cooked")]));          // no serves
        Assert.NotNull(MealRules.OpProblem(OpKind.Create, [new("Name", "Risoni"), new("ServesPerBatch", "4")]));     // no kind
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Name", " Risoni")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Kind", "cooked")]));                                 // by name, exactly
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Kind", "0")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", "0")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", "100")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", "04")]));                           // not the one spelling
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", "4.5")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("ServesPerBatch", null)]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("FixedCostCents", "0")]));                            // free is NoCost
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("FixedCostCents", "45.00")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Notes", "")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Notes", " padded ")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Notes", new string('a', MealRules.MaxNotesLength + 1))]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("GroupId", "Beef")]));
        Assert.NotNull(MealRules.OpProblem(OpKind.Update, [new("Ingredients", "beef, pasta")]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \n ", null)]
    [InlineData("  Double the sauce.\nFreezes well.  ", "Double the sauce.\nFreezes well.")]
    public void Notes_are_trimmed_and_none_is_null(string? typed, string? saved) => Assert.Equal(saved, MealRules.NormaliseNotes(typed));

    [Fact]
    public void A_new_meal_sends_every_field_it_has_and_an_edit_only_what_changed()
    {
        MealDraft draft = new("Spaghetti bolognese", Beef, MealKind.Cooked, 4, null, null);

        Assert.Equal(["Name", "GroupId", "Kind", "ServesPerBatch"], draft.FieldsChangedFrom(null).Select(f => f.Name));
        Assert.Empty(draft.FieldsChangedFrom(draft));
        Assert.Equal([new OpField("ServesPerBatch", "6")], (draft with { ServesPerBatch = 6 }).FieldsChangedFrom(draft));
        Assert.Equal([new OpField("Kind", "FixedCost"), new OpField("FixedCostCents", "4500")],
            (draft with { Kind = MealKind.FixedCost, FixedCostCents = 4500 }).FieldsChangedFrom(draft));
        Assert.Equal([new OpField("GroupId", null), new OpField("Notes", "Freezes well.")],
            (draft with { GroupId = null, Notes = "Freezes well." }).FieldsChangedFrom(draft));
    }
}
