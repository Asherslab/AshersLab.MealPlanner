using Microsoft.Data.Sqlite;

namespace AshersLab.MealPlanner.Grpc.Data;

/// <summary>
/// A self-contained copy of the live database, safe to back up while the app writes - and the way back from it.
/// </summary>
/// <remarks>
/// A Home Assistant hot backup tars <c>/data</c> while the add-on runs. The live database file copied mid-write, or
/// without the <c>-wal</c> holding its newest commits, can be unusable or silently old. So the add-on's
/// <c>backup_pre</c> runs <see cref="TakeAsync"/> (<c>dotnet AshersLab.MealPlanner.Grpc.dll snapshot</c>), which
/// writes one consistent file with SQLite's <c>VACUUM INTO</c> - a read transaction, so it sees exactly the commits
/// before it started and never blocks the app's writer - and <c>backup_exclude</c> leaves the live file, its
/// <c>-wal</c> and its <c>-shm</c> out of the backup. A restore therefore brings back only the snapshot, and
/// <see cref="RestoreIfMissing"/> puts it in place on the next start. See docs/modules/infrastructure/deployment.md.
/// </remarks>
public static class DatabaseSnapshot
{
    /// <summary>
    /// Beside the database. Its name must not match the add-on's <c>backup_exclude</c> patterns
    /// (<c>mealplanner.db</c>, <c>-wal</c>, <c>-shm</c>), which the Supervisor matches against the end of each path.
    /// </summary>
    public const string FileName = "mealplanner.snapshot.db";

    public const string Verb = "snapshot";

    /// <summary>
    /// Writes the snapshot next to <paramref name="databasePath"/>, replacing any earlier one only once the new one
    /// has passed <c>PRAGMA integrity_check</c>. Throws when it cannot - which fails the Home Assistant backup, loudly,
    /// rather than backing up nothing.
    /// </summary>
    public static async Task<string> TakeAsync(string databasePath, CancellationToken ct = default)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("There is no database to snapshot.", databasePath);

        string snapshot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, FileName);
        string partial  = snapshot + ".partial";

        // VACUUM INTO refuses to overwrite; a partial file is a failed earlier attempt.
        File.Delete(partial);
        try
        {
            // Not ReadOnly: a read-only open of a WAL database fails when its -shm is missing (the app stopped). It
            // only reads - VACUUM INTO writes nothing to the source.
            await using (SqliteConnection live = new(new SqliteConnectionStringBuilder
                         {
                             DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 30,
                         }.ToString()))
            {
                await live.OpenAsync(ct);
                await using SqliteCommand vacuum = live.CreateCommand();
                vacuum.CommandText = "VACUUM INTO $path";
                vacuum.Parameters.AddWithValue("$path", partial);
                await vacuum.ExecuteNonQueryAsync(ct);
            }

            if (await IntegrityAsync(partial, ct) is var result and not "ok")
                throw new InvalidOperationException($"The snapshot failed its integrity check: {result}");

            File.Move(partial, snapshot, overwrite: true);
            return snapshot;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    /// <summary>
    /// After a Home Assistant restore, <c>/data</c> holds the snapshot and no live database (it was excluded from
    /// the backup): copy the snapshot into place. Returns whether it did. A live database that exists is never touched.
    /// </summary>
    public static bool RestoreIfMissing(string databasePath, ILogger logger)
    {
        string snapshot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, FileName);
        if (File.Exists(databasePath) || !File.Exists(snapshot))
            return false;

        // A -wal or -shm with no database cannot belong to the snapshot; replayed onto it, it would corrupt it.
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
        File.Copy(snapshot, databasePath);

        logger.LogWarning("No database at {Database}; restored it from the backup snapshot {Snapshot} ({Taken:u})",
            databasePath, snapshot, File.GetLastWriteTimeUtc(snapshot));
        return true;
    }

    public static async Task<string> IntegrityAsync(string path, CancellationToken ct = default)
    {
        await using SqliteConnection db = new(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        await db.OpenAsync(ct);
        await using SqliteCommand check = db.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        return (string)(await check.ExecuteScalarAsync(ct))!;
    }
}
