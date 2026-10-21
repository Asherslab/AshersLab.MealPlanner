using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>Calendar entries as the op log sees them, who eats one, and what saving the entry sheet sends.</summary>
public class CalendarRulesTests
{
    private static readonly Guid Sam     = new("01900000-0000-7000-8000-00000000000a");
    private static readonly Guid Alex    = new("01900000-0000-7000-8000-00000000000b");
    private static readonly Guid Noodles = new("01900000-0000-7000-8000-0000000000c1");
    private static readonly Guid Soup    = new("01900000-0000-7000-8000-0000000000c2");
    private static readonly Guid Pizza   = new("01900000-0000-7000-8000-0000000000c3");
    private static readonly Guid Lunch   = new("01900000-0000-7000-8000-0000000000d1");
    private static readonly Guid Dinner  = new("01900000-0000-7000-8000-0000000000d2");
    private static readonly DateOnly Tue = new(2026, 10, 27);
    private static readonly DateOnly Thu = new(2026, 10, 29);

    /// <summary>A plan's entry on Tuesday's dinner, usually noodles, for Sam, nothing changed.</summary>
    private static readonly CalendarEntryNow Planned =
        new(FromPlan: true, Usual: Noodles, PlannedPeople: [Sam], MealField: null, PeopleField: null, CalendarStatus.Planned, Tue, Dinner, null, null);

    private static CalendarEntryDraft As(CalendarEntryNow now) => new(now.Meal, now.People, now.Status == CalendarStatus.Skipped, now.Day, now.InSlot);

    private static Dictionary<string, string?> Fields(IReadOnlyList<OpField> fields) => fields.ToDictionary(f => f.Name, f => f.Value);

    [Fact]
    public void An_entry_needs_a_date_and_a_slot_and_each_field_has_one_spelling()
    {
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Create, CalendarEntryRules.CreateFields(new GeneratedEntry(Guid.CreateVersion7(), Noodles, Tue, Dinner))));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Create, [new("Date", "2026-10-27"), new("SlotId", OpValues.FormatId(Dinner)), new("MealId", OpValues.FormatId(Soup))]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Create, [new("Date", "2026-10-27")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("Date", "2026-10-7")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("Date", null)]));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("Status", "Skipped")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("Status", "skipped")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("Status", "1")]));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("MovedDate", null), new("MovedSlotId", null)]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("MovedDate", "tomorrow")]));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("People", "everyone")]));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("People", PlanPeople.Format([Sam]))]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("People", "Everyone")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("MealId", Noodles.ToString("N"))]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("Week", "1")]));
    }

    [Fact]
    public void Creating_a_planned_entry_carries_only_what_never_changes()
    {
        // A stale device applying the plan again sends this create after a skip or a move made elsewhere: carrying the
        // status or where it is would undo them.
        IReadOnlyList<OpField> fields = CalendarEntryRules.CreateFields(new GeneratedEntry(Guid.CreateVersion7(), Noodles, Tue, Dinner));
        Assert.Equal(["TemplateEntryId", "Date", "SlotId"], fields.Select(f => f.Name));
        Assert.Equal("2026-10-27", Fields(fields)["Date"]);
    }

    [Fact]
    public void A_plan_starts_on_a_Monday_or_not_yet()
    {
        Assert.Null(PlanTemplateRules.OpProblem(OpKind.Update, [new("StartsOn", "2026-10-26")]));
        Assert.Null(PlanTemplateRules.OpProblem(OpKind.Update, [new("StartsOn", null)]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new("StartsOn", "2026-10-27")]));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new("StartsOn", "26/10/2026")]));
    }

    [Fact]
    public void Only_a_change_the_planner_made_touches_an_entry()
    {
        Assert.False(CalendarEntryRules.Touched(null, null, CalendarStatus.Planned, null, null));
        Assert.True(CalendarEntryRules.Touched(Soup, null, CalendarStatus.Planned, null, null));
        Assert.True(CalendarEntryRules.Touched(null, "everyone", CalendarStatus.Planned, null, null));
        Assert.True(CalendarEntryRules.Touched(null, null, CalendarStatus.Skipped, null, null));
        Assert.True(CalendarEntryRules.Touched(null, null, CalendarStatus.Planned, Thu, Lunch));
    }

    [Fact]
    public void Who_eats_it_is_as_planned_everyone_or_some_people()
    {
        Assert.Equal([Sam], CalendarPeople.Resolve(null, [Sam]));
        Assert.Null(CalendarPeople.Resolve(null, null));
        Assert.Null(CalendarPeople.Resolve("everyone", [Sam]));
        Assert.Equal([Alex], CalendarPeople.Resolve(PlanPeople.Format([Alex]), [Sam]));

        Assert.Null(CalendarPeople.Field([Sam], [Sam]));                    // as planned
        Assert.Null(CalendarPeople.Field(null, null));
        Assert.Equal("everyone", CalendarPeople.Field(null, [Sam]));       // everyone, this once
        Assert.Equal(PlanPeople.Format([Alex, Sam]), CalendarPeople.Field([Sam, Alex], null));
        Assert.Null(CalendarPeople.Field([], null));                       // nobody is not an entry: everyone
    }

    [Fact]
    public void Saving_an_unchanged_entry_sends_nothing()
    {
        Assert.Empty(As(Planned).ChangesFrom(Planned));
        CalendarEntryNow changed = Planned with { MealField = Soup, PeopleField = "everyone", Status = CalendarStatus.Skipped, MovedDate = Thu, MovedSlot = Lunch };
        Assert.Empty(As(changed).ChangesFrom(changed));
    }

    private static readonly string? SamText = PlanPeople.Format([Sam]);

    [Fact]
    public void Choosing_another_meal_sends_only_the_meal_and_the_usual_one_again_goes_back_to_as_planned()
    {
        Assert.Equal(new Dictionary<string, string?> { ["MealId"] = OpValues.FormatId(Soup) },
            Fields((As(Planned) with { Meal = Soup }).ChangesFrom(Planned)));

        CalendarEntryNow soup = Planned with { MealField = Soup };
        Assert.Equal(new Dictionary<string, string?> { ["MealId"] = OpValues.FormatId(Pizza) }, Fields((As(soup) with { Meal = Pizza }).ChangesFrom(soup)));
        Assert.Equal(new Dictionary<string, string?> { ["MealId"] = null }, Fields((As(soup) with { Meal = Noodles }).ChangesFrom(soup)));
    }

    [Fact]
    public void An_entry_added_by_hand_always_names_its_meal()
    {
        CalendarEntryNow byHand = new(FromPlan: false, Usual: null, PlannedPeople: null, MealField: Noodles, PeopleField: null, CalendarStatus.Planned, Tue, Dinner, null, null);
        Assert.Equal(Noodles, byHand.Meal);
        Assert.Null(byHand.People);
        Assert.Equal(new Dictionary<string, string?> { ["MealId"] = OpValues.FormatId(Soup) }, Fields((As(byHand) with { Meal = Soup }).ChangesFrom(byHand)));
        Assert.Equal(new Dictionary<string, string?> { ["People"] = PlanPeople.Format([Sam]) }, Fields((As(byHand) with { People = [Sam] }).ChangesFrom(byHand)));
    }

    [Fact]
    public void Who_eats_it_this_time_is_sent_only_when_it_differs_from_the_plan()
    {
        Assert.Equal(new Dictionary<string, string?> { ["People"] = "everyone" },
            Fields((As(Planned) with { People = null }).ChangesFrom(Planned)));
        Assert.Equal(new Dictionary<string, string?> { ["People"] = PlanPeople.Format([Alex, Sam]) },
            Fields((As(Planned) with { People = [Alex, Sam] }).ChangesFrom(Planned)));
        CalendarEntryNow everyone = Planned with { PeopleField = "everyone" };
        Assert.Null(everyone.People);
        Assert.Equal(new Dictionary<string, string?> { ["People"] = null }, Fields((As(everyone) with { People = [Sam] }).ChangesFrom(everyone)));
    }

    [Fact]
    public void Skipping_and_unskipping_send_only_the_status()
    {
        // Never the meal or people: another device may have chosen them meanwhile, and a stale copy would undo that.
        Assert.Equal(new Dictionary<string, string?> { ["Status"] = "Skipped" }, Fields((As(Planned) with { Skipped = true }).ChangesFrom(Planned)));
        CalendarEntryNow skipped = Planned with { MealField = Soup, Status = CalendarStatus.Skipped };
        Assert.Equal(new Dictionary<string, string?> { ["Status"] = "Planned" }, Fields((As(skipped) with { Skipped = false }).ChangesFrom(skipped)));
    }

    [Fact]
    public void A_move_sends_both_its_fields_and_moving_home_clears_them()
    {
        Assert.Equal(new Dictionary<string, string?> { ["MovedDate"] = "2026-10-29", ["MovedSlotId"] = OpValues.FormatId(Dinner) },
            Fields((As(Planned) with { Day = Thu }).ChangesFrom(Planned)));
        Assert.Equal(new Dictionary<string, string?> { ["MovedDate"] = "2026-10-27", ["MovedSlotId"] = OpValues.FormatId(Lunch) },
            Fields((As(Planned) with { Slot = Lunch }).ChangesFrom(Planned)));

        CalendarEntryNow moved = Planned with { MovedDate = Thu, MovedSlot = Lunch };
        Assert.Equal((Thu, Lunch), (moved.Day, moved.InSlot));
        Assert.Equal(new Dictionary<string, string?> { ["MovedDate"] = OpValues.FormatDate(Thu), ["MovedSlotId"] = OpValues.FormatId(Dinner) },
            Fields((As(moved) with { Slot = Dinner }).ChangesFrom(moved)));
        Assert.Equal(new Dictionary<string, string?> { ["MovedDate"] = null, ["MovedSlotId"] = null },
            Fields((As(moved) with { Day = Tue, Slot = Dinner }).ChangesFrom(moved)));
    }

    [Fact]
    public void Replanning_a_skipped_entry_moves_and_unskips_it_in_one_op()
    {
        CalendarEntryNow skipped = Planned with { Status = CalendarStatus.Skipped };
        Assert.Equal(new Dictionary<string, string?> { ["Status"] = "Planned", ["MovedDate"] = "2026-10-29", ["MovedSlotId"] = OpValues.FormatId(Lunch) },
            Fields((As(skipped) with { Skipped = false, Day = Thu, Slot = Lunch }).ChangesFrom(skipped)));
    }

    [Fact]
    public void A_skip_follows_its_plan_entry_and_names_no_meal_once_it_is_gone()
    {
        // The plan entry removed: no usual meal, nobody planned - the price of never writing out what was not changed.
        CalendarEntryNow gone = Planned with { Status = CalendarStatus.Skipped, Usual = null, PlannedPeople = null };
        Assert.Null(gone.Meal);
        Assert.Null(gone.People);
        // One the planner had given its own meal keeps it.
        Assert.Equal(Soup, (gone with { MealField = Soup }).Meal);
    }

    [Fact]
    public void The_batch_a_lunch_eats_and_the_serves_a_batch_made_are_fields_of_their_own()
    {
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("EatsFrom", OpValues.FormatId(Noodles)), new("ServesMade", "6")]));
        Assert.Null(CalendarEntryRules.OpProblem(OpKind.Update, [new("EatsFrom", null), new("ServesMade", null)]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("ServesMade", "0")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("ServesMade", "100")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("ServesMade", "06")]));
        Assert.NotNull(CalendarEntryRules.OpProblem(OpKind.Update, [new("EatsFrom", "monday")]));

        // Either one set is the planner's change: the entry stays when the plan moves.
        Assert.True(CalendarEntryRules.Touched(null, null, CalendarStatus.Planned, null, null, eatsFrom: Soup));
        Assert.True(CalendarEntryRules.Touched(null, null, CalendarStatus.Planned, null, null, servesMade: 6));

        // Sent only when changed, and alone.
        Guid batch = Pizza;
        Assert.Equal(new Dictionary<string, string?> { ["EatsFrom"] = OpValues.FormatId(batch) },
            Fields((As(Planned) with { EatsFrom = batch }).ChangesFrom(Planned)));
        Assert.Equal(new Dictionary<string, string?> { ["ServesMade"] = "6" },
            Fields((As(Planned) with { ServesMade = 6 }).ChangesFrom(Planned)));
        CalendarEntryNow set = Planned with { EatsFrom = batch, ServesMade = 6 };
        Assert.Empty((As(Planned) with { EatsFrom = batch, ServesMade = 6 }).ChangesFrom(set));
        Assert.Equal(new Dictionary<string, string?> { ["EatsFrom"] = null, ["ServesMade"] = null }, Fields(As(Planned).ChangesFrom(set)));
    }

    [Fact]
    public void A_plan_entry_whose_choices_are_gone_has_no_meal_until_one_is_chosen()
    {
        CalendarEntryNow orphan = Planned with { Usual = null };
        Assert.Null(orphan.Meal);
        Assert.Equal(new Dictionary<string, string?> { ["MealId"] = OpValues.FormatId(Soup) },
            Fields((As(orphan) with { Meal = Soup }).ChangesFrom(orphan)));
    }
}
