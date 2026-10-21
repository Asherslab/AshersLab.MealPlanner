using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Tests.Domain;

/// <summary>A plan's start or week count changed from a date on, and every day before it keeping the rotation it had.</summary>
public class RotationPhasesTests
{
    private static readonly DateOnly Oct5  = new(2026, 10, 5);
    private static readonly DateOnly Oct26 = new(2026, 10, 26);
    private static readonly DateOnly Nov2  = new(2026, 11, 2);
    private static readonly DateOnly Nov16 = new(2026, 11, 16);
    private static readonly DateOnly Nov23 = new(2026, 11, 23);

    [Fact]
    public void Each_day_is_in_the_rotation_of_its_phase()
    {
        IReadOnlyList<RotationPhase> earlier = [new(Nov2, Oct5, 4), new(Nov16, Oct26, 2)];
        Assert.Equal((Oct5, 4), RotationPhases.On(earlier, Nov16, 3, new DateOnly(2026, 1, 1)));
        Assert.Equal((Oct5, 4), RotationPhases.On(earlier, Nov16, 3, Nov2.AddDays(-1)));
        Assert.Equal((Oct26, 2), RotationPhases.On(earlier, Nov16, 3, Nov2));
        Assert.Equal((Oct26, 2), RotationPhases.On(earlier, Nov16, 3, Nov16.AddDays(-1)));
        Assert.Equal((Nov16, 3), RotationPhases.On(earlier, Nov16, 3, Nov16));
        Assert.Equal(((DateOnly?)null, 4), RotationPhases.On([], null, 4, Nov16));
    }

    [Fact]
    public void A_change_keeps_the_days_before_it_and_cuts_off_every_later_change()
    {
        // The first change: everything before 2 Nov is the old rotation.
        Assert.Equal([new RotationPhase(Nov2, Oct5, 4)], RotationPhases.Change([], Oct5, 4, Nov2, Nov2, 4));

        // A change before an earlier one replaces it: the phase is cut at the new date, the later one gone.
        IReadOnlyList<RotationPhase> twice = [new(Nov2, Oct5, 4), new(Nov23, Oct26, 2)];
        Assert.Equal([new RotationPhase(Nov2, Oct5, 4), new RotationPhase(Nov16, Oct26, 2)],
            RotationPhases.Change(twice, Nov23, 3, Nov16, Nov16, 1));
        Assert.Equal([new RotationPhase(Oct26, Oct5, 4)], RotationPhases.Change(twice, Nov23, 3, Oct26, Oct26, 1));

        // Never applied: nothing to keep.
        Assert.Empty(RotationPhases.Change([], null, 4, Nov2, Nov2, 4));
    }

    [Fact]
    public void Changing_back_to_the_rotation_before_folds_into_it()
    {
        IReadOnlyList<RotationPhase> once = RotationPhases.Change([], Oct5, 4, Nov2, Oct26, 4);
        Assert.Equal([new RotationPhase(Nov2, Oct5, 4)], once);
        // From 2 Nov back to the old start: no phase is needed at all.
        Assert.Empty(RotationPhases.Change(once, Oct26, 4, Nov2, Oct5, 4));
    }

    [Fact]
    public void The_field_has_one_spelling()
    {
        IReadOnlyList<RotationPhase> phases = [new(Nov2, Oct5, 4), new(Nov16, Oct26, 12)];
        string text = RotationPhases.Format(phases)!;
        Assert.Equal("2026-11-02|2026-10-05|4;2026-11-16|2026-10-26|12", text);
        Assert.True(RotationPhases.TryParse(text, out IReadOnlyList<RotationPhase> back));
        Assert.Equal(phases, back);
        Assert.Null(RotationPhases.Format([]));

        Assert.True(RotationPhases.TryParse(null, out _));
        Assert.False(RotationPhases.TryParse("", out _));
        Assert.False(RotationPhases.TryParse("2026-11-02|2026-10-06|4", out _));                          // not a Monday
        Assert.False(RotationPhases.TryParse("2026-11-02|2026-10-05|13", out _));                         // too many weeks
        Assert.False(RotationPhases.TryParse("2026-11-16|2026-10-05|4;2026-11-02|2026-10-26|2", out _));  // untils not rising
        Assert.False(RotationPhases.TryParse("2026-11-02|2026-10-05", out _));
        Assert.NotNull(PlanTemplateRules.OpProblem(OpKind.Update, [new(PlanTemplateRules.EarlierField, "soon")]));
        Assert.Null(PlanTemplateRules.OpProblem(OpKind.Update, [new(PlanTemplateRules.EarlierField, text)]));
    }

    [Fact]
    public void A_plans_rotation_change_sends_only_what_differs_and_nothing_for_the_rotation_it_already_has()
    {
        Dictionary<string, string?> Fields(IReadOnlyList<OpField> f) => f.ToDictionary(x => x.Name, x => x.Value);

        // First applied: just the start.
        Assert.Equal(new Dictionary<string, string?> { ["StartsOn"] = "2026-10-26" },
            Fields(PlanTemplateRules.RotationChange(null, null, 4, Oct26, Oct26, 4)));
        // A new start from 2 Nov: the old one kept for the days before.
        Assert.Equal(new Dictionary<string, string?> { ["StartsOn"] = "2026-11-02", ["Earlier"] = "2026-11-02|2026-10-26|4" },
            Fields(PlanTemplateRules.RotationChange(null, Oct26, 4, Nov2, Nov2, 4)));
        // Fewer weeks from 16 Nov, same start.
        Assert.Equal(new Dictionary<string, string?> { ["WeekCount"] = "3", ["Earlier"] = "2026-11-16|2026-10-26|4" },
            Fields(PlanTemplateRules.RotationChange(null, Oct26, 4, Nov16, Oct26, 3)));
        // An older week filled as the week it was: nothing.
        Assert.Empty(PlanTemplateRules.RotationChange("2026-11-02|2026-10-26|4", Nov2, 4, Oct26, Oct26, 4));
        Assert.Empty(PlanTemplateRules.RotationChange(null, Oct26, 4, Nov16, Oct26, 4));
    }
}
