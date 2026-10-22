using AshersLab.MealPlanner.Shared.Domain.Appearance;

namespace AshersLab.MealPlanner.Tests.Domain;

public class AppearanceRulesTests
{
    [Theory]
    [InlineData("dark", Appearance.Dark)]
    [InlineData("light", Appearance.Light)]
    [InlineData(null, Appearance.Light)]
    [InlineData("", Appearance.Light)]
    [InlineData("Dark", Appearance.Light)]       // stored values are exact; nothing but "dark" is the night
    [InlineData("sepia", Appearance.Light)]      // a value from a later version reads as the light, not an error
    public void A_stored_value_reads_as_the_light_unless_it_is_dark(string? value, Appearance expected) =>
        Assert.Equal(expected, AppearanceRules.Parse(value));

    [Fact]
    public void Names_round_trip_and_only_they_may_be_sent()
    {
        foreach (Appearance a in Enum.GetValues<Appearance>())
        {
            Assert.Equal(a, AppearanceRules.Parse(AppearanceRules.Name(a)));
            Assert.True(AppearanceRules.Valid(AppearanceRules.Name(a)));
        }
        Assert.False(AppearanceRules.Valid(null));
        Assert.False(AppearanceRules.Valid("DARK"));
    }

    [Theory]
    // Chosen on this phone, not yet on the account: shown, and sent - unless the account says the same already.
    [InlineData(Appearance.Dark, true, null, Appearance.Dark, true)]
    [InlineData(Appearance.Dark, true, "light", Appearance.Dark, true)]
    [InlineData(Appearance.Dark, true, "dark", Appearance.Dark, false)]
    [InlineData(Appearance.Light, true, "dark", Appearance.Light, true)]
    // Nothing unsent: the account's choice, made on another phone, is shown here too.
    [InlineData(Appearance.Light, false, "dark", Appearance.Dark, false)]
    [InlineData(Appearance.Dark, false, "light", Appearance.Light, false)]
    // An account that never chose leaves the phone as it is, and nothing is written for it.
    [InlineData(Appearance.Dark, false, null, Appearance.Dark, false)]
    [InlineData(Appearance.Light, false, null, Appearance.Light, false)]
    public void An_unsent_choice_wins_otherwise_the_accounts_is_shown(Appearance device, bool unsent, string? account, Appearance show, bool send) =>
        Assert.Equal(new AppearancePlan(show, send), AppearanceRules.Reconcile(device, unsent, account));
}
