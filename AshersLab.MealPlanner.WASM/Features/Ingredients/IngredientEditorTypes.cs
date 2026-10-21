using AshersLab.MealPlanner.Shared.Domain.Ingredients;

namespace AshersLab.MealPlanner.WASM.Features.Ingredients;

/// <summary>What the editor answers: the edited ingredient, or a confirmed delete.</summary>
public sealed record IngredientEditorResult(IngredientDraft? Saved, bool Delete);

/// <summary>A choice in the editor's category or store picker.</summary>
public sealed record PickerOption(Guid Id, string Name);
