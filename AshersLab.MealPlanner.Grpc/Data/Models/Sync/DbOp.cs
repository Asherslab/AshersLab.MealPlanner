using AshersLab.MealPlanner.Shared.Domain.Sync;

namespace AshersLab.MealPlanner.Grpc.Data.Models.Sync;

/// <summary>
/// One accepted op, in the order the server accepted it. What every browser pulls. Append-only: never
/// updated, never deleted (compaction is not needed in version 1).
/// </summary>
public class DbOp
{
    /// <summary>
    /// The server's order, from SQLite's rowid. Gap-free in the order pulls see it only because SQLite has one
    /// writer at a time: a push's ops are numbered and committed before the next push can number any. On a
    /// database with concurrent writers (Postgres) a later seq could commit first and a pull could skip the
    /// earlier one - revisit before moving.
    /// </summary>
    public long Seq { get; set; }

    public Guid OpId { get; set; }

    public Guid DeviceId { get; set; }

    public required string Hlc { get; set; }

    public required string Entity { get; set; }

    public Guid EntityId { get; set; }

    public OpKind Kind { get; set; }

    /// <summary><c>Shared.Data.Sync.OpFieldsJson</c>.</summary>
    public required string FieldsJson { get; set; }

    public DateTime ReceivedAtUtc { get; set; }
}
