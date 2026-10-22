using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>People, slots, plans and their entries as the op log sees them; and what saving an entry sends.</summary>
public class PlanningRulesTests
{
    private static readonly Guid A = new("01900000-0000-7000-8000-00000000000a");
    private static readonly Guid B = new("01900000-0000-7000-8000-00000000000b");
    private static readonly Guid C = new("01900000-0000-7000-8000-00000000000c");

    [Fact]
    public void A_person_needs_a_name_and_a_colour_spelt_one_way()
    {
        Assert.Null(PersonRules.OpProblem(OpKind.Create, [new("Name", "Sam"), new("Colour", "#1e88e5")]));
        Assert.NotNull(PersonRules.OpProblem(OpKind.Create, [new("Name", "Sam")]));
        Assert.NotNull(PersonRules.OpProblem(OpKind.Update, [new("Colour", "#1E88E5")]));     // one spelling
        Assert.NotNull(PersonRules.OpProblem(OpKind.Update, [new("Colour", "blue")]));
        Assert.NotNull(PersonRules.OpProblem(OpKind.Update, [new("Colour", null)]));
        Assert.NotNull(PersonRules.OpProblem(OpKind.Update, [new("Name", " Sam")]));
    }

    [Fact]
    public void A_portion_is_a_quarter_to_two_serves_in_quarters_and_null_is_a_full_serve()
    {
        foreach (string ok in new[] { "0.25", "0.5", "0.75", "1", "1.5", "2" })
            Assert.Null(PersonRules.OpProblem(OpKind.Update, [new("Portion", ok)]));
        Assert.Null(PersonRules.OpProblem(OpKind.Update, [new("Portion", null)]));
        foreach (string bad in new[] { "0", "-0.5", "0.3", "2.25", "half", "" })
            Assert.NotNull(PersonRules.OpProblem(OpKind.Update, [new("Portion", bad)]));
        Assert.Equal(["a quarter serve", "half a serve", "three quarters of a serve", "a full serve", "1¼ serves", "1½ serves", "2 serves"],
            PersonRules.Portions.Select(PersonRules.PortionText));
    }

    [Fact]
    public void Someone_new_gets_the_first_colour_nobody_has()
    {
        Assert.Equal(PersonRules.Palette[0], PersonRules.NextColour([]));
        Assert.Equal(PersonRules.Palette[2], PersonRules.NextColour([PersonRules.Palette[0], PersonRules.Palette[1]]));
        Assert.Equal(PersonRules.Palette[0], PersonRules.NextColour(PersonRules.Palette));
    }

    [Fact]
    public void A_plan_runs_one_to_twelve_weeks()
    {
        Assert.Null(PlanTemplateRules.OpProblem(OpKind.Create, [new("Name", "Rotation"), new("WeekCount", "4")]));
        Assert.Null(PlanTemplateRules.OpProblem(OpKind.Update, [new("WeekCount", "12")]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new("WeekCount", "0")]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new("WeekCount", "13")]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new("WeekCount", "04")]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Create, [new("Name", "Rotation")]));
    }

    [Fact]
    public void A_slot_is_a_name_and_a_position()
    {
        Assert.Null(SlotRules.OpProblem(OpKind.Create, [new("Name", "Snack"), new("Position", "2")]));
        Assert.NotNull(SlotRules.OpProblem(OpKind.Create, [new("Name", "Snack")]));
        Assert.NotNull(SlotRules.OpProblem(OpKind.Update, [new("Position", "two")]));
    }

    [Fact]
    public void An_entry_is_a_week_a_day_by_name_a_slot_and_who_eats_it()
    {
        string template = OpValues.FormatId(A)!, slot = OpValues.FormatId(B)!;
        Assert.Null(TemplateEntryRules.OpProblem(OpKind.Create,
            [new("TemplateId", template), new("Week", "1"), new("Day", "Monday"), new("SlotId", slot)]));
        Assert.Null(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", PlanPeople.Format([B, A]))]));
        Assert.Null(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", null)]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Create, [new("TemplateId", template), new("Week", "1"), new("Day", "Monday")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("Day", "monday")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("Day", "1")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("Week", "0")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("Week", "13")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("SlotId", null)]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", "")]));
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", $"{B},{A}")]));          // not sorted
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", $"{A},{A}")]));          // twice
        Assert.NotNull(TemplateEntryRules.OpProblem(OpKind.Update, [new("People", $"{A}, {B}")]));
        Assert.Null(TemplateChoiceRules.OpProblem(OpKind.Create, [new("EntryId", template), new("MealId", slot)]));
        Assert.NotNull(TemplateChoiceRules.OpProblem(OpKind.Create, [new("EntryId", template)]));
    }

    [Fact]
    public void Who_eats_it_has_one_spelling_and_nobody_is_everyone()
    {
        Assert.Null(PlanPeople.Format(null));
        Assert.Null(PlanPeople.Format([]));
        Assert.Equal(PlanPeople.Format([A, B]), PlanPeople.Format([B, A, B]));
        Assert.True(PlanPeople.TryParse(PlanPeople.Format([C, A]), out IReadOnlyList<Guid>? people));
        Assert.Equal([A, C], people);
        Assert.True(PlanPeople.TryParse(null, out people));
        Assert.Null(people);
        Assert.False(PlanPeople.TryParse(string.Join(',', Enumerable.Range(1, PlanPeople.Max + 1)
            .Select(i => new Guid($"01900000-0000-7000-8000-{i:000000000000}")).Select(g => g.ToString("D"))), out _));
    }

    [Theory]
    [InlineData("Monday", DayOfWeek.Monday)]
    [InlineData("mon", DayOfWeek.Monday)]
    [InlineData("Tues", DayOfWeek.Tuesday)]
    [InlineData("SUN", DayOfWeek.Sunday)]
    [InlineData("Mo", null)]
    [InlineData("Weekly cost", null)]
    public void A_sheet_day_is_read_by_its_first_letters(string text, DayOfWeek? day) => Assert.Equal(day, PlanDays.Read(text));

    [Fact]
    public void A_new_entry_sends_its_people_and_a_choice_per_meal()
    {
        TemplateEntryChange change = new TemplateEntryDraft([A, B, A], [C]).ChangesFrom(null);
        Assert.Equal([new OpField("People", OpValues.FormatId(C))], change.EntryFields);
        Assert.Equal([A, B], change.AddMeals);
        Assert.Empty(change.RemoveChoices);
        Assert.Empty(new TemplateEntryDraft([A], null).ChangesFrom(null).EntryFields);
    }

    [Fact]
    public void Saving_an_entry_unchanged_sends_nothing()
    {
        Guid row1 = Guid.CreateVersion7(), row2 = Guid.CreateVersion7();
        Assert.True(new TemplateEntryDraft([A, B], [C]).ChangesFrom(([C], [(row1, A), (row2, B)])).IsEmpty);
    }

    [Fact]
    public void A_removed_choice_goes_an_added_one_comes_last_and_who_eats_it_is_sent_when_it_changed()
    {
        Guid row1 = Guid.CreateVersion7(), row2 = Guid.CreateVersion7();
        TemplateEntryChange change = new TemplateEntryDraft([A, C], null).ChangesFrom(([B], [(row1, A), (row2, B)]));

        Assert.Equal([new OpField("People", null)], change.EntryFields);
        Assert.Equal([row2], change.RemoveChoices);
        Assert.Equal([C], change.AddMeals);
    }

    [Fact]
    public void Making_another_meal_the_usual_one_rewrites_the_rows_after_it_since_rows_list_by_id()
    {
        Guid row1 = Guid.CreateVersion7(), row2 = Guid.CreateVersion7(), row3 = Guid.CreateVersion7();
        TemplateEntryChange change = new TemplateEntryDraft([C, A, B], null).ChangesFrom((null, [(row1, A), (row2, B), (row3, C)]));

        // C's row is kept and is first by id once A's and B's are made again after it.
        Assert.Equal([row1, row2], change.RemoveChoices);
        Assert.Equal([A, B], change.AddMeals);
        Assert.Empty(change.EntryFields);
    }

    [Fact]
    public void A_meal_chosen_twice_by_two_phones_is_one_choice_again()
    {
        Guid row1 = Guid.CreateVersion7(), row2 = Guid.CreateVersion7();
        TemplateEntryChange change = new TemplateEntryDraft([A], null).ChangesFrom((null, [(row1, A), (row2, A)]));
        Assert.Equal([row2], change.RemoveChoices);
        Assert.Empty(change.AddMeals);
    }
}
