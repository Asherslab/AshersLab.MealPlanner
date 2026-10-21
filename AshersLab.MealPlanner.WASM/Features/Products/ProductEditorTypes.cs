using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Products;

namespace AshersLab.MealPlanner.WASM.Features.Products;

/// <summary>What the product editor answers: the edited product, or a confirmed delete.</summary>
public sealed record ProductEditorResult(ProductDraft? Saved, bool Delete);

/// <summary>A choice in the product editor's ingredient picker, with the unit its pack size is read in.</summary>
public sealed record IngredientOption(Guid Id, string Name, BaseUnit Unit);
