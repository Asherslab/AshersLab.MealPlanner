using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using MudBlazor;
using MudBlazor.Services;

namespace AshersLab.MealPlanner.WASM.Features.Ingredients;

/// <summary>What the editor answers: the edited ingredient, or a confirmed delete.</summary>
public sealed record IngredientEditorResult(IngredientDraft? Saved, bool Delete);

/// <summary>A choice in the editor's category or store picker.</summary>
public sealed record PickerOption(Guid Id, string Name);

public static class IngredientDialogs
{
    /// <summary>
    /// Opens the <see cref="IngredientEditor"/> - full screen on a phone - and waits for its answer; null when cancelled.
    /// The Ingredients page and a meal's "new ingredient" both open it, so a new one is made the same way from either.
    /// </summary>
    public static async Task<IngredientEditorResult?> EditAsync(
        IDialogService              dialogs,
        IBrowserViewportService     viewport,
        IngredientDraft             draft,
        bool                        isNew,
        IReadOnlyList<PickerOption> categories,
        IReadOnlyList<PickerOption> stores,
        IReadOnlyList<string>       otherNames,
        IReadOnlyList<string>?      usedByProducts = null,
        IReadOnlyList<string>?      usedByMeals = null
    )
    {
        bool phone = await viewport.GetCurrentBreakpointAsync() < Breakpoint.Sm;

        DialogParameters<IngredientEditor> parameters = new()
        {
            { e => e.Draft, draft },
            { e => e.IsNew, isNew },
            { e => e.Categories, categories },
            { e => e.Stores, stores },
            { e => e.OtherNames, otherNames },
            { e => e.UsedByProducts, usedByProducts ?? [] },
            { e => e.UsedByMeals, usedByMeals ?? [] },
        };
        DialogOptions options = new() { FullWidth = true, MaxWidth = MaxWidth.Small, FullScreen = phone, CloseOnEscapeKey = true };

        IDialogReference dialog = await dialogs.ShowAsync<IngredientEditor>(isNew ? "New ingredient" : "Edit ingredient", parameters, options);
        DialogResult? result = await dialog.Result;
        return result is { Canceled: false, Data: IngredientEditorResult answer } ? answer : null;
    }
}
