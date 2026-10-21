using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

public class MealIngredientRulesTests
{
    private static readonly Guid Meal  = new("0199f0d2-0000-7000-8000-00000000000a");
    private static readonly Guid Mince = new("0199f0d2-0000-7000-8000-000000000001");

    [Fact]
    public void Accepts_a_create_and_each_kind_of_edit()
    {
        Assert.Null(MealIngredientRules.OpProblem(OpKind.Create, [new("MealId", Meal.ToString()), new("IngredientId", Mince.ToString())]));
        Assert.Null(MealIngredientRules.OpProblem(OpKind.Create,
            [new("MealId", Meal.ToString()), new("IngredientId", Mince.ToString()), new("Quantity", "500"), new("Note", "browned")]));
        Assert.Null(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "0.25")]));
        Assert.Null(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", null), new("Note", null)]));
        Assert.Null(MealIngredientRules.OpProblem(OpKind.Delete, []));
    }

    [Fact]
    public void Refuses_what_the_server_could_not_apply()
    {
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Create, [new("MealId", Meal.ToString())]));                // no ingredient
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Create, [new("IngredientId", Mince.ToString())]));         // no meal
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("MealId", null)]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("IngredientId", null)]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("IngredientId", "mince")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "500.0")]));                      // not the one spelling
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "0")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "-5")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "0.0001")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Quantity", "1000001")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Note", "")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Note", " browned")]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Note", new string('a', MealIngredientRules.MaxNoteLength + 1))]));
        Assert.NotNull(MealIngredientRules.OpProblem(OpKind.Update, [new("Unit", "Gram")]));
    }

    [Theory]
    [InlineData("500g", BaseUnit.Gram, "500")]
    [InlineData("1.5kg", BaseUnit.Gram, "1500")]
    [InlineData(" 2 x 250 g ", BaseUnit.Gram, "500")]
    [InlineData("250", BaseUnit.Gram, "250")]
    [InlineData("1L", BaseUnit.Millilitre, "1000")]
    [InlineData("2", BaseUnit.Each, "2")]
    [InlineData("0.5", BaseUnit.Each, "0.5")]
    public void A_quantity_is_typed_as_the_planner_says_it_and_held_in_the_base_unit(string typed, BaseUnit unit, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            MealIngredientRules.ParseQuantity(typed, unit, out string? problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_quantity_is_not_known_yet_not_a_mistake(string? typed)
    {
        Assert.Null(MealIngredientRules.ParseQuantity(typed, BaseUnit.Gram, out string? problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("500ml", BaseUnit.Gram, "It is counted in g")]
    [InlineData("a handful", BaseUnit.Gram, "Type a quantity like")]
    [InlineData("1,5kg", BaseUnit.Gram, "Type a quantity like")]
    [InlineData("0", BaseUnit.Gram, "Type how much it uses, or leave it empty.")]
    [InlineData("2000kg", BaseUnit.Gram, "That is more than any recipe uses.")]
    [InlineData("0.0001", BaseUnit.Each, "Keep it to 3 decimal places.")]
    public void A_quantity_that_cannot_be_read_is_refused_saying_why(string typed, BaseUnit unit, string because)
    {
        Assert.Null(MealIngredientRules.ParseQuantity(typed, unit, out string? problem));
        Assert.StartsWith(because, problem);
    }

    [Fact]
    public void A_new_line_sends_every_field_it_has_and_an_edit_only_what_changed()
    {
        MealIngredientDraft draft = new(Meal, Mince, null, null);

        Assert.Equal([new OpField("MealId", Meal.ToString()), new OpField("IngredientId", Mince.ToString())], draft.FieldsChangedFrom(null));
        Assert.Empty(draft.FieldsChangedFrom(draft));
        Assert.Equal([new OpField("Quantity", "500")], (draft with { Quantity = 500.000m }).FieldsChangedFrom(draft));
        Assert.Empty((draft with { Quantity = 500.000m }).FieldsChangedFrom(draft with { Quantity = 500m }));
        Assert.Equal([new OpField("Quantity", null), new OpField("Note", "browned")],
            (draft with { Note = "browned" }).FieldsChangedFrom(draft with { Quantity = 500m }));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" for  the sauce ", "for the sauce")]
    public void A_note_is_normalised_and_none_is_null(string? typed, string? saved) => Assert.Equal(saved, MealIngredientRules.NormaliseNote(typed));
}
