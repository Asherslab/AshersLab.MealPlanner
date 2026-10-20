using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Grpc.Features.Sync;

public static class SyncStartup
{
    /// <summary>
    /// After migrating, before serving: mints this database's epoch on its first start and primes
    /// <see cref="SyncSignal"/> with the newest seq. Not seed data in a migration - every database would then
    /// share one epoch, and a wiped server would look like the same database to every browser.
    /// </summary>
    public static async Task InitialiseAsync(MealPlannerDbContext db, SyncSignal signal)
    {
        DbSyncEpoch? epoch = await db.SyncEpochs.SingleOrDefaultAsync();
        if (epoch is null)
        {
            epoch = new DbSyncEpoch { Id = 1, Epoch = Guid.CreateVersion7(), CreatedAtUtc = DateTime.UtcNow };
            db.SyncEpochs.Add(epoch);
            await db.SaveChangesAsync();
        }

        signal.Initialise(epoch.Epoch, await db.Ops.MaxAsync(o => (long?)o.Seq) ?? 0);
    }
}
