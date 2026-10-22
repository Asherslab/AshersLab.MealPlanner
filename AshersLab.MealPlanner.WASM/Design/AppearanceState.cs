using AshersLab.MealPlanner.Shared.Domain.Appearance;
using AshersLab.MealPlanner.WASM.Account;
using Microsoft.JSInterop;

namespace AshersLab.MealPlanner.WASM.Design;

/// <summary>
/// Light or dark: the planner's choice on Settings → Colours, kept with their sign-in so it follows them to every phone,
/// and never the phone's own dark mode (docs/modules/infrastructure/accounts.md#light-or-dark).
/// </summary>
/// <remarks>
/// The device keeps a copy (<c>js/appearance.js</c>, read in &lt;head&gt;), so the first paint is already right and the
/// choice works offline. A choice that has not reached the account yet - made offline, signed out - is marked unsent and
/// sent when it can; otherwise the account's wins, as chosen on another phone (<see cref="AppearanceRules.Reconcile"/>).
/// Where the app has no sign-in of its own (Home Assistant's frame, a sign-in proxy, development) the device's copy is all
/// there is.
/// </remarks>
public sealed class AppearanceState
{
    private readonly IJSRuntime js;
    private readonly AccountClient account;
    private bool _unsent;

    public AppearanceState(IJSRuntime js, AccountClient account)
    {
        (this.js, this.account) = (js, account);
        // Signed in on this phone: their choice from another, or this one's that was waiting for them.
        account.SignedIn += ReconcileAsync;
    }

    public Appearance Current { get; private set; }

    public bool IsDark => Current == Appearance.Dark;

    /// <summary>Chosen here and not yet with the account: made offline or signed out.</summary>
    public bool Unsent => _unsent;

    /// <summary>Whether the choice is kept with a sign-in (the app's own) or on this device only; null until the server has said.</summary>
    public bool? WithAccount { get; private set; }

    /// <summary>The colours, or where they are kept, changed - here or on another phone.</summary>
    public event Action? Changed;

    /// <summary>This device's copy, read once at start, before the first render.</summary>
    public async Task LoadAsync()
    {
        // [theme, "1" when unsent]: plain strings, which trimming cannot break.
        string?[] stored = await js.InvokeAsync<string?[]>("mealPlannerAppearance.read");
        (Current, _unsent) = (AppearanceRules.Parse(stored.ElementAtOrDefault(0)), stored.ElementAtOrDefault(1) == "1");
    }

    /// <summary>The planner chose: shown at once, kept here, and sent to their account.</summary>
    public async Task ChooseAsync(Appearance appearance)
    {
        await ShowAsync(appearance, unsent: true);
        await ReconcileAsync();
    }

    /// <summary>Squares this device with the account: at start, after signing in, and after a choice.</summary>
    public async Task ReconcileAsync()
    {
        AccountState state = await account.StateAsync();
        bool? before = WithAccount;
        WithAccount = state.Available;
        if (!state.Available)
        {
            // No sign-in of the app's own: nothing to send it to, so nothing is waiting.
            await ShowAsync(Current, unsent: false, kept: before != WithAccount);
            return;
        }
        if (!state.SignedIn)
        {
            await ShowAsync(Current, _unsent, kept: before != WithAccount);   // kept here, unsent if chosen, until they sign in
            return;
        }

        AppearancePlan plan = AppearanceRules.Reconcile(Current, _unsent, state.Appearance);
        bool waiting = plan.Send && !(await account.SetAppearanceAsync(AppearanceRules.Name(plan.Show))).Ok;
        await ShowAsync(plan.Show, waiting, kept: before != WithAccount);
    }

    /// <param name="kept">Where the choice is kept changed, so the page has news even if the colours did not.</param>
    private async Task ShowAsync(Appearance appearance, bool unsent, bool kept = false)
    {
        bool changed = appearance != Current || unsent != _unsent;
        if (changed)
        {
            (Current, _unsent) = (appearance, unsent);
            await js.InvokeVoidAsync("mealPlannerAppearance.apply", AppearanceRules.Name(appearance), unsent);
        }
        if (changed || kept)
            Changed?.Invoke();
    }
}
