namespace AshersLab.MealPlanner.Shared.Domain.Sync;

/// <summary>
/// One changed field of an op. The value is text in invariant culture whatever the field's type, so the op
/// log has one shape for every entity; each entity's rules parse their own fields.
/// </summary>
public sealed record OpField(string Name, string? Value);
