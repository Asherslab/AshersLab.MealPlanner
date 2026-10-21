using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.WASM.Features.Ingredients;
using MudBlazor;
using MudBlazor.Services;

namespace AshersLab.MealPlanner.WASM.Features.Planning;

/// <summary>Someone the entry sheet can name.</summary>
public sealed record PersonOption(Guid Id, string Name, string Colour);

/// <summary>What the entry sheet answers: the entry as saved, or "remove it".</summary>
public sealed record TemplateEntrySheetResult(TemplateEntryDraft? Saved, bool Remove);

/// <summary>What the plan editor answers: the new name and weeks, or a confirmed delete.</summary>
public sealed record PlanEditorResult(string? Name, int Weeks, bool Delete);

/// <summary>What the entry sheet needs to know of a meal: whether it cooks a batch or eats one, and its serves per batch.</summary>
public sealed record MealServesOption(MealKind Kind, int ServesPerBatch);

/// <summary>
/// What the calendar entry sheet answers: the entry as saved (skipped or not, and where), or "remove it", or "make a new
/// meal with this name" - <see cref="Saved"/> then holds what was chosen so far, with no meal, for the sheet to open with again.
/// </summary>
public sealed record CalendarEntrySheetResult(CalendarEntryDraft? Saved, bool Remove, string? NewMeal = null);

/// <summary>A plan the calendar can be filled from, and which of its weeks the week on screen is now.</summary>
public sealed record ApplyPlanOption(Guid Id, string Name, int Weeks, int WeekOnScreen);

/// <summary>Fill from this plan, the week on screen being week <paramref name="Week"/>, for <paramref name="Count"/> weeks.</summary>
public sealed record ApplyPlanResult(Guid Plan, int Week, int Count);

public static class PlanDialogs
{
    /// <summary>Opens the <see cref="CalendarEntrySheet"/> - full screen on a phone - and waits; null when cancelled.</summary>
    public static async Task<CalendarEntrySheetResult?> EditCalendarEntryAsync(
        IDialogService              dialogs,
        IBrowserViewportService     viewport,
        CalendarEntryDraft          draft,
        bool                        isNew,
        string                      where,
        string?                     source,
        IReadOnlyList<PickerOption> choices,
        IReadOnlyList<PickerOption> meals,
        IReadOnlyList<PersonOption> people,
        IReadOnlyList<PickerOption> slots,
        IReadOnlyDictionary<Guid, MealServesOption> mealServes,
        IReadOnlyList<PickerOption> batches
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<CalendarEntrySheet> parameters = new()
        {
            { s => s.MealServes, mealServes },
            { s => s.Batches, batches },
            { s => s.Draft, draft },
            { s => s.IsNew, isNew },
            { s => s.Where, where },
            { s => s.Source, source },
            { s => s.Choices, choices },
            { s => s.Meals, meals },
            { s => s.People, people },
            { s => s.Slots, slots },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.Small, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<CalendarEntrySheet>(isNew ? "Add to the calendar" : "On the calendar", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: CalendarEntrySheetResult answer } ? answer : null;
    }

    /// <summary>Opens the <see cref="ApplyPlanDialog"/> and waits; null when cancelled.</summary>
    public static async Task<ApplyPlanResult?> ApplyPlanAsync(
        IDialogService                 dialogs,
        IBrowserViewportService        viewport,
        IReadOnlyList<ApplyPlanOption> plans,
        DateOnly                       monday
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;
        DialogParameters<ApplyPlanDialog> parameters = new() { { d => d.Plans, plans }, { d => d.Monday, monday } };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.ExtraSmall, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<ApplyPlanDialog>("Fill from a plan", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: ApplyPlanResult answer } ? answer : null;
    }

    /// <summary>Opens the <see cref="TemplateEntrySheet"/> - full screen on a phone - and waits; null when cancelled.</summary>
    public static async Task<TemplateEntrySheetResult?> EditEntryAsync(
        IDialogService              dialogs,
        IBrowserViewportService     viewport,
        TemplateEntryDraft          draft,
        bool                        isNew,
        string                      where,
        IReadOnlyList<PickerOption> meals,
        IReadOnlyList<PersonOption> people
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<TemplateEntrySheet> parameters = new()
        {
            { s => s.Draft, draft },
            { s => s.IsNew, isNew },
            { s => s.Where, where },
            { s => s.Meals, meals },
            { s => s.People, people },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.Small, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<TemplateEntrySheet>(isNew ? "Add to the plan" : "Plan entry", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: TemplateEntrySheetResult answer } ? answer : null;
    }

    /// <summary>Opens the <see cref="PlanEditor"/> and waits; null when cancelled.</summary>
    public static async Task<PlanEditorResult?> EditPlanAsync(
        IDialogService          dialogs,
        IBrowserViewportService viewport,
        string                  name,
        int                     weeks,
        IReadOnlyList<string>   otherNames,
        IReadOnlyDictionary<int, int> entriesByWeek
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<PlanEditor> parameters = new()
        {
            { e => e.Name, name },
            { e => e.Weeks, weeks },
            { e => e.OtherNames, otherNames },
            { e => e.EntriesByWeek, entriesByWeek },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.ExtraSmall, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<PlanEditor>("Edit plan", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: PlanEditorResult answer } ? answer : null;
    }
}
