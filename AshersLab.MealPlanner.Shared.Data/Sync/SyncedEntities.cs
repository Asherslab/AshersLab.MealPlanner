using AshersLab.MealPlanner.Shared.Data.Ingredients;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Sync;

/// <summary>
/// Every entity type the op log carries, and their tables. Both databases call <see cref="BuildModel"/>, so
/// the browser's and the server's synced tables cannot drift apart - only each side's own tables (the
/// browser's outbox, the server's log) differ.
/// </summary>
/// <remarks>
/// <b>A new synced entity is: its row class, its <see cref="SyncedEntity{TRow}"/>, a line in
/// <see cref="All"/> and its table in <see cref="BuildModel"/> - then a migration on both sides.</b> An op
/// for a type missing from <see cref="All"/> is rejected by the server and never written by the browser.
/// </remarks>
public static class SyncedEntities
{
    public static readonly IngredientEntity Ingredient = new();

    public static readonly IReadOnlyList<SyncedEntity> All = [Ingredient];

    private static readonly Dictionary<string, SyncedEntity> ByName = All.ToDictionary(e => e.Entity, StringComparer.Ordinal);

    public static SyncedEntity? Find(string entity) => ByName.GetValueOrDefault(entity);

    public static void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldClock>(clock =>
        {
            clock.ToTable("FieldClock");
            clock.HasKey(c => new { c.Entity, c.EntityId, c.Field });
        });

        modelBuilder.Entity<Ingredient>(ingredient =>
        {
            ingredient.ToTable("Ingredient");
            ingredient.HasKey(i => i.Id);
            // Minted by the device that created it, never by the database.
            ingredient.Property(i => i.Id).ValueGeneratedNever();
            ingredient.Property(i => i.Name).HasMaxLength(Domain.Ingredients.IngredientRules.MaxNameLength);
        });
    }
}
