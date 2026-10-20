using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.LocalData;

public static class DeviceRegistration
{
    /// <summary>
    /// Records that the app has started in this browser: creates the <see cref="Device"/> row on the first
    /// start, otherwise counts one more start against the existing row and keeps its id.
    /// </summary>
    /// <remarks>
    /// The id is a GUIDv7 like every other client-minted id (docs/work/2026-10-offline-sync.md).
    /// </remarks>
    public static async Task<Device> RecordOpenAsync(
        LocalDbContext    db,
        DateTimeOffset    now,
        CancellationToken cancellationToken = default
    )
    {
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

        return device;
    }
}
