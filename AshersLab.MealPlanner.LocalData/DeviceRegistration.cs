using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AshersLab.MealPlanner.LocalData;

public static class DeviceRegistration
{
    /// <summary>
    /// Records that the app has started in this browser: creates the <see cref="Device"/> row on the first
    /// start, otherwise counts one more start against the existing row and keeps its id.
    /// </summary>
    /// <remarks>
    /// The id is a GUIDv7 like every other client-minted id (docs/modules/sync/offline-sync.md).
    /// <para>
    /// Read and write in one IMMEDIATE transaction: two windows of the app starting together share the one
    /// database, and without it both could find no row and each insert one.
    /// </para>
    /// </remarks>
    public static async Task<Device> RecordOpenAsync(
        LocalDbContext    db,
        DateTimeOffset    now,
        CancellationToken cancellationToken = default
    )
    {
        // Serializable is how both SQLite providers spell BEGIN IMMEDIATE.
        await using IDbContextTransaction transaction =
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        Device? device = await db.Devices.SingleOrDefaultAsync(cancellationToken);

        if (device is null)
        {
            device = new Device
            {
                Id            = Guid.CreateVersion7(now),
                FirstOpenedAt = now,
            };
            db.Devices.Add(device);
        }

        device.LastOpenedAt =  now;
        device.OpenCount    += 1;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return device;
    }
}
