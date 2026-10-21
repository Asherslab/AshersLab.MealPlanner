using AshersLab.MealPlanner.Shared.Domain.Pantry;

namespace AshersLab.MealPlanner.WASM.Features.Pantry;

/// <summary>What the pantry sheet answers: a movement to add, or one to delete.</summary>
public sealed record PantrySheetResult(StockEntry? Entry, Guid? DeleteId);
