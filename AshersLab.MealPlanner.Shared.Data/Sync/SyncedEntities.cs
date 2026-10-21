using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Pantry;
using AshersLab.MealPlanner.Shared.Data.Planning;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Shopping;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Domain;
using AshersLab.MealPlanner.Shared.Domain.Meals;
using AshersLab.MealPlanner.Shared.Domain.Planning;
using AshersLab.MealPlanner.Shared.Domain.Products;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

    public static readonly ProductEntity Product = new();

    public static readonly PriceObservationEntity PriceObservation = new();

    public static readonly MealGroupEntity MealGroup = new();

    public static readonly MealEntity Meal = new();

    public static readonly MealIngredientEntity MealIngredient = new();

    public static readonly PersonEntity Person = new();

    public static readonly SlotEntity Slot = new();

    public static readonly PlanTemplateEntity PlanTemplate = new();

    public static readonly TemplateEntryEntity TemplateEntry = new();

    public static readonly TemplateChoiceEntity TemplateChoice = new();

    public static readonly CalendarEntryEntity CalendarEntry = new();

    public static readonly StockMovementEntity StockMovement = new();

    public static readonly ShoppingListEntity ShoppingList = new();

    public static readonly ShoppingListItemEntity ShoppingListItem = new();

    public static readonly IReadOnlyList<SyncedEntity> All =
    [
        Ingredient, Store, Category, Product, PriceObservation, MealGroup, Meal, MealIngredient,
        Person, Slot, PlanTemplate, TemplateEntry, TemplateChoice, CalendarEntry, StockMovement, ShoppingList, ShoppingListItem,
    ];

    // Text in the op's own format, converted here rather than by the provider: see PriceObservation.ObservedOn.
    private static readonly ValueConverter<DateOnly, string> DateText =
        new(date => OpValues.FormatDate(date), text => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture));

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
            // By name, as the op carries it, so the table reads the same as the log.
            ingredient.Property(i => i.BaseUnit).HasConversion<string>().HasMaxLength(20);
            ingredient.Property(i => i.StapleStock).HasConversion<string>().HasMaxLength(20);
            // Indexed for "is this category / store still used?", the check before a delete. Not foreign keys.
            ingredient.HasIndex(i => i.CategoryId);
            ingredient.HasIndex(i => i.UsualStoreId);
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

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Product");
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).ValueGeneratedNever();
            product.Property(p => p.Name).HasMaxLength(Names.MaxLength);
            product.Property(p => p.StoreCode).HasMaxLength(ProductRules.MaxStoreCodeLength);
            // A product's ingredient's products, and "is this store / ingredient still used?" Not foreign keys.
            product.HasIndex(p => p.IngredientId);
            product.HasIndex(p => p.StoreId);
        });

        modelBuilder.Entity<PriceObservation>(price =>
        {
            price.ToTable("PriceObservation");
            price.HasKey(p => p.Id);
            price.Property(p => p.Id).ValueGeneratedNever();
            // By name, as the op carries it.
            price.Property(p => p.Source).HasConversion<string>().HasMaxLength(20);
            // Text in the op's own format, converted here rather than by the provider: the browser's SQLite reader
            // (SqliteWasmBlazor) throws InvalidCastException on GetFieldValue<DateOnly>, though the server's reads it.
            price.Property(p => p.ObservedOn)
                .HasConversion(date => OpValues.FormatDate(date), text => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture))
                .HasMaxLength(10);
            // A product's history. Not a foreign key.
            price.HasIndex(p => p.ProductId);
        });

        modelBuilder.Entity<MealGroup>(group =>
        {
            group.ToTable("MealGroup");
            group.HasKey(g => g.Id);
            group.Property(g => g.Id).ValueGeneratedNever();
            group.Property(g => g.Name).HasMaxLength(Names.MaxLength);
        });

        modelBuilder.Entity<Meal>(meal =>
        {
            meal.ToTable("Meal");
            meal.HasKey(m => m.Id);
            meal.Property(m => m.Id).ValueGeneratedNever();
            meal.Property(m => m.Name).HasMaxLength(Names.MaxLength);
            // By name, as the op carries it.
            meal.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
            meal.Property(m => m.Notes).HasMaxLength(MealRules.MaxNotesLength);
            // "Is this group still used?", the check before a delete. Not a foreign key.
            meal.HasIndex(m => m.GroupId);
        });

        modelBuilder.Entity<MealIngredient>(line =>
        {
            line.ToTable("MealIngredient");
            line.HasKey(l => l.Id);
            line.Property(l => l.Id).ValueGeneratedNever();
            line.Property(l => l.Note).HasMaxLength(MealIngredientRules.MaxNoteLength);
            // A meal's ingredients, and "is this ingredient still used?" Not foreign keys.
            line.HasIndex(l => l.MealId);
            line.HasIndex(l => l.IngredientId);
        });

        modelBuilder.Entity<Person>(person =>
        {
            person.ToTable("Person");
            person.HasKey(p => p.Id);
            person.Property(p => p.Id).ValueGeneratedNever();
            person.Property(p => p.Name).HasMaxLength(Names.MaxLength);
            person.Property(p => p.Colour).HasMaxLength(7);
        });

        modelBuilder.Entity<Slot>(slot =>
        {
            slot.ToTable("Slot");
            slot.HasKey(s => s.Id);
            slot.Property(s => s.Id).ValueGeneratedNever();
            slot.Property(s => s.Name).HasMaxLength(Names.MaxLength);
        });

        modelBuilder.Entity<PlanTemplate>(template =>
        {
            template.ToTable("PlanTemplate");
            template.HasKey(t => t.Id);
            template.Property(t => t.Id).ValueGeneratedNever();
            template.Property(t => t.Name).HasMaxLength(Names.MaxLength);
            template.Property(t => t.StartsOn).HasConversion(DateText).HasMaxLength(10);
        });

        modelBuilder.Entity<TemplateEntry>(entry =>
        {
            entry.ToTable("TemplateEntry");
            entry.HasKey(e => e.Id);
            entry.Property(e => e.Id).ValueGeneratedNever();
            // By name, as the op carries it.
            entry.Property(e => e.Day).HasConversion<string>().HasMaxLength(10);
            // At most PlanPeople.Max ids of 36 characters and their commas.
            entry.Property(e => e.People).HasMaxLength(PlanPeople.Max * 37);
            // A template's entries, and "is this slot still used?" Not foreign keys.
            entry.HasIndex(e => e.TemplateId);
            entry.HasIndex(e => e.SlotId);
        });

        modelBuilder.Entity<TemplateChoice>(choice =>
        {
            choice.ToTable("TemplateChoice");
            choice.HasKey(c => c.Id);
            choice.Property(c => c.Id).ValueGeneratedNever();
            // An entry's choices, and "is this meal still planned?" Not foreign keys.
            choice.HasIndex(c => c.EntryId);
            choice.HasIndex(c => c.MealId);
        });

        modelBuilder.Entity<CalendarEntry>(entry =>
        {
            entry.ToTable("CalendarEntry");
            entry.HasKey(e => e.Id);
            // Minted by the device - or, for an entry a plan made, derived from its template entry and date (Rotation.EntryId).
            entry.Property(e => e.Id).ValueGeneratedNever();
            entry.Property(e => e.Date).HasConversion(DateText).HasMaxLength(10);
            entry.Property(e => e.MovedDate).HasConversion(DateText).HasMaxLength(10);
            // By name, as the op carries it.
            entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(10);
            entry.Property(e => e.People).HasMaxLength(PlanPeople.Max * 37);
            // The days a page shows, and what applying a plan over them already holds. Not foreign keys.
            entry.HasIndex(e => e.Date);
            entry.HasIndex(e => e.MovedDate);
            entry.HasIndex(e => e.TemplateEntryId);
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.ToTable("StockMovement");
            movement.HasKey(m => m.Id);
            movement.Property(m => m.Id).ValueGeneratedNever();
            // By name, as the op carries it.
            movement.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.OccurredOn).HasConversion(DateText).HasMaxLength(10);
            // An ingredient's ledger. Not a foreign key.
            movement.HasIndex(m => m.IngredientId);
        });

        modelBuilder.Entity<ShoppingList>(list =>
        {
            list.ToTable("ShoppingList");
            list.HasKey(l => l.Id);
            // Derived from its dates (ShoppingListRules.ListId), so two devices make the same list.
            list.Property(l => l.Id).ValueGeneratedNever();
            list.Property(l => l.StartsOn).HasConversion(DateText).HasMaxLength(10);
            list.Property(l => l.EndsOn).HasConversion(DateText).HasMaxLength(10);
        });

        modelBuilder.Entity<ShoppingListItem>(item =>
        {
            item.ToTable("ShoppingListItem");
            item.HasKey(i => i.Id);
            // A tick's id is derived from its list and ingredient (ShoppingItemRules.TickId); an extra's is minted.
            item.Property(i => i.Id).ValueGeneratedNever();
            item.Property(i => i.Name).HasMaxLength(Names.MaxLength);
            // By name, as the op carries it.
            item.Property(i => i.Tick).HasConversion<string>().HasMaxLength(10);
            // A list's items. Not a foreign key.
            item.HasIndex(i => i.ListId);
        });
    }
}
