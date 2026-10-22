namespace AshersLab.MealPlanner.Shared.Domain.Appearance;

/// <summary>The app's colours: the pad by day, or at night. The planner's choice, never the phone's own setting.</summary>
public enum Appearance
{
    Light,
    Dark,
}

/// <summary>What this device does about the account's choice, once it has heard from the server.</summary>
/// <param name="Show">The colours to show now.</param>
/// <param name="Send">Whether this device's choice still has to reach the account.</param>
public sealed record AppearancePlan(Appearance Show, bool Send);

/// <summary>
/// Light or dark, kept with the planner's sign-in so it follows them to every phone (docs/modules/infrastructure/accounts.md).
/// Each device also keeps its own copy, so the first paint is already right and the choice works offline; one made
/// offline waits there, unsent, and wins when it gets through - it is the newer.
/// </summary>
public static class AppearanceRules
{
    /// <summary>"light" or "dark", as the server and the device store it.</summary>
    public static string Name(Appearance appearance) => appearance == Appearance.Dark ? "dark" : "light";

    /// <summary>A stored value read back. Anything else - nothing chosen yet, a value from a later version - is the light.</summary>
    public static Appearance Parse(string? value) => value == "dark" ? Appearance.Dark : Appearance.Light;

    /// <summary>Whether <paramref name="value"/> is one a device may send.</summary>
    public static bool Valid(string? value) => value is "light" or "dark";

    /// <summary>
    /// This device's copy against the account's: an unsent choice of this device's goes to the account (unless the account
    /// already says the same); otherwise the account's is shown - chosen on another phone since. An account that never
    /// chose leaves this device as it is.
    /// </summary>
    public static AppearancePlan Reconcile(Appearance device, bool unsent, string? account) =>
        unsent ? new AppearancePlan(device, Send: !Valid(account) || Parse(account) != device)
        : Valid(account) ? new AppearancePlan(Parse(account), Send: false)
        : new AppearancePlan(device, Send: false);
}
