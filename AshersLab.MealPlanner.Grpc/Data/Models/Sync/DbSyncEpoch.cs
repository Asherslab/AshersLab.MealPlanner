namespace AshersLab.MealPlanner.Grpc.Data.Models.Sync;

/// <summary>
/// One row: which server database this is. Minted when the database is first started, so a database that is
/// wiped and started again gets a new one, and every browser's "pulled through seq N" - which meant something
/// only in the old database - is recognised as stale and replaced by a snapshot.
/// </summary>
/// <remarks>
/// A restore from an older backup keeps its epoch; that case is caught instead by a browser asking for a seq
/// the log has not reached (<c>SyncService.Pull</c>).
/// </remarks>
public class DbSyncEpoch
{
    public int Id { get; set; }

    public Guid Epoch { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
