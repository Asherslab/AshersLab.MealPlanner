using System.Data;
using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using AshersLab.MealPlanner.Grpc.Features.Catalogue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AshersLab.MealPlanner.Grpc.Features.Sync;

public static class SyncStartup
{
    /// <summary>
    /// After migrating, before serving: mints this database's epoch on its first start, writes any catalogue seed
    /// it has never held (<see cref="CatalogueSeed"/>), and primes <see cref="SyncSignal"/> with the newest seq.
    /// The epoch is not seed data in a migration - every database would then share one epoch, and a wiped server
    /// would look like the same database to every browser.
    /// </summary>
    public static async Task InitialiseAsync(MealPlannerDbContext db, SyncSignal signal)
    {
        await using (IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            if (!await db.SyncEpochs.AnyAsync())
                db.SyncEpochs.Add(new DbSyncEpoch { Id = 1, Epoch = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow });

            await db.SaveChangesAsync();
            await CatalogueSeed.WriteMissingAsync(db);
            await transaction.CommitAsync();
        }

        DbSyncEpoch epoch = await db.SyncEpochs.SingleAsync();

        signal.Initialise(epoch.Epoch, await db.Ops.MaxAsync(o => (long?)o.Seq) ?? 0);
    }
}
