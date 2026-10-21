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

public static class PlanDialogs
{
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
