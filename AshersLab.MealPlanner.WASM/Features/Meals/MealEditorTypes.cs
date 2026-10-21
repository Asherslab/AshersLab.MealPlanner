using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.WASM.Features.Ingredients;
using AshersLab.MealPlanner.WASM.Features.Products;
using MudBlazor;
using MudBlazor.Services;

namespace AshersLab.MealPlanner.WASM.Features.Meals;

/// <summary>What the meal editor answers: the edited meal, or a confirmed delete.</summary>
public sealed record MealEditorResult(MealDraft? Saved, bool Delete);

/// <summary>What the ingredient sheet answers: the line as saved, or "remove it".</summary>
public sealed record MealIngredientSheetResult(MealIngredientDraft? Saved, bool Remove);

public static class MealDialogs
{
    /// <summary>Opens the <see cref="MealIngredientSheet"/> - full screen on a phone - and waits for its answer; null when cancelled.</summary>
    public static async Task<MealIngredientSheetResult?> EditLineAsync(
        IDialogService                  dialogs,
        IBrowserViewportService         viewport,
        MealIngredientDraft             draft,
        bool                            isNew,
        IReadOnlyList<IngredientOption> ingredients,
        string?                         goneName = null
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<MealIngredientSheet> parameters = new()
        {
            { s => s.Draft, draft },
            { s => s.IsNew, isNew },
            { s => s.Ingredients, ingredients },
            { s => s.GoneName, goneName },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.Small, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<MealIngredientSheet>(isNew ? "Add an ingredient" : "Ingredient", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: MealIngredientSheetResult answer } ? answer : null;
    }

    /// <summary>Opens the <see cref="MealEditor"/> - full screen on a phone - and waits for its answer; null when cancelled.</summary>
    public static async Task<MealEditorResult?> EditAsync(
        IDialogService             dialogs,
        IBrowserViewportService    viewport,
        MealDraft                  draft,
        bool                       isNew,
        IReadOnlyList<PickerOption> groups,
        IReadOnlyList<string>      otherNames,
        IReadOnlyList<string>?     plannedIn = null
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<MealEditor> parameters = new()
        {
            { e => e.Draft, draft },
            { e => e.IsNew, isNew },
            { e => e.Groups, groups },
            { e => e.OtherNames, otherNames },
            { e => e.PlannedIn, plannedIn ?? [] },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.Small, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<MealEditor>(isNew ? "New meal" : "Edit meal", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: MealEditorResult answer } ? answer : null;
    }
}
