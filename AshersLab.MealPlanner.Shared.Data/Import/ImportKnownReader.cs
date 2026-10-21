using AshersLab.MealPlanner.Shared.Data.Categories;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Meals;
using AshersLab.MealPlanner.Shared.Data.Prices;
using AshersLab.MealPlanner.Shared.Data.Products;
using AshersLab.MealPlanner.Shared.Data.Stores;
using AshersLab.MealPlanner.Shared.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Shared.Data.Import;

/// <summary>
/// What the spreadsheet import compares the sheet with (<see cref="SheetImport"/>): every synced row it matches by
/// name, tombstones included - something deleted here is not brought back. One read per table.
/// </summary>
public static class ImportKnownReader
{
    public static async Task<ImportKnown> ReadAsync(DbContext db, CancellationToken cancellationToken = default) =>
        new(
            await db.Set<Ingredient>().AsNoTracking()
                .Select(i => new KnownIngredient(i.Id, i.Name, i.BaseUnit, i.Deleted, i.UnitGuessed, i.UsualStoreId)).ToListAsync(cancellationToken),
            await db.Set<Category>().AsNoTracking()
                .Select(c => new KnownOrdered(c.Id, c.Name, c.Position, c.Deleted)).ToListAsync(cancellationToken),
            await db.Set<MealGroup>().AsNoTracking()
                .Select(g => new KnownOrdered(g.Id, g.Name, g.Position, g.Deleted)).ToListAsync(cancellationToken),
            await db.Set<Meal>().AsNoTracking()
                .Select(m => new KnownNamed(m.Id, m.Name, m.Deleted)).ToListAsync(cancellationToken),
            await db.Set<Store>().AsNoTracking()
                .Select(s => new KnownNamed(s.Id, s.Name, s.Deleted)).ToListAsync(cancellationToken),
            await db.Set<Product>().AsNoTracking()
                .Select(p => new KnownProduct(p.Id, p.Name, p.StoreId, p.IngredientId, p.PackQuantity, p.Deleted)).ToListAsync(cancellationToken),
            await db.Set<PriceObservation>().AsNoTracking()
                .Select(p => new KnownPrice(p.Id, p.ProductId, p.PriceCents, p.ObservedOn, p.Deleted, p.Source)).ToListAsync(cancellationToken));
}
