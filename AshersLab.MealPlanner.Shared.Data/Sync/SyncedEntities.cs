using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Domain;
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
/// <para>
/// <b>No foreign keys between synced tables.</b> Ops arrive in any order, so an ingredient can name a store
/// this database has not heard of yet, or one deleted on another device; a constraint would reject the op and
/// the databases would diverge. A reference to a missing or deleted row is shown as gone
/// (docs/modules/catalogue/catalogue.md).
/// </para>
/// </remarks>
public static class SyncedEntities
{
    public static readonly IngredientEntity Ingredient = new();

    public static readonly StoreEntity Store = new();

    public static readonly CategoryEntity Category = new();

    public static readonly IReadOnlyList<SyncedEntity> All = [Ingredient, Store, Category];

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
            ingredient.Property(i => i.Name).HasMaxLength(Names.MaxLength);
        });

        modelBuilder.Entity<Store>(store =>
        {
            store.ToTable("Store");
            store.HasKey(s => s.Id);
            store.Property(s => s.Id).ValueGeneratedNever();
            store.Property(s => s.Name).HasMaxLength(Names.MaxLength);
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.ToTable("Category");
            category.HasKey(c => c.Id);
            category.Property(c => c.Id).ValueGeneratedNever();
            category.Property(c => c.Name).HasMaxLength(Names.MaxLength);
        });
    }
}
