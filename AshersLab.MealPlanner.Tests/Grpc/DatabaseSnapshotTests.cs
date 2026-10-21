using AshersLab.MealPlanner.Grpc.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AshersLab.MealPlanner.Tests.Grpc;

/// <summary>
/// The Home Assistant backup snapshot: a copy taken while the app writes is whole and consistent, and a restore that
/// brought back only the snapshot starts from it.
/// </summary>
public sealed class DatabaseSnapshotTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _dir = Directory.CreateTempSubdirectory("mealplanner-snapshot-").FullName;

    private string Db => Path.Combine(_dir, DataDirectory.DatabaseFileName);
    private string Snapshot => Path.Combine(_dir, DatabaseSnapshot.FileName);

    [Fact]
    public async Task A_snapshot_taken_under_write_load_is_whole_and_consistent()
    {
        // The app's shape: WAL, one writer committing continuously. Every transaction writes a pair of rows and bumps a
        // counter, so a copy that caught half a transaction, or a page from one and a page from the next, shows it.
        await using (SqliteConnection setup = Open(Db))
            await ExecAsync(setup, """
                PRAGMA journal_mode = WAL;
                CREATE TABLE Pair (Id INTEGER PRIMARY KEY, Txn INTEGER NOT NULL, Payload TEXT NOT NULL);
                CREATE TABLE Counter (Id INTEGER PRIMARY KEY, Txns INTEGER NOT NULL);
                INSERT INTO Counter VALUES (1, 0);
                """);

        using CancellationTokenSource stop = new();
        long written = 0;
        Task writer = Task.Run(async () =>
        {
            await using SqliteConnection db = Open(Db);
            string payload = new('x', 2000);     // a few rows per page, so the file grows by pages as it is copied
            while (!stop.IsCancellationRequested)
            {
                long txn = Interlocked.Increment(ref written);
                await ExecAsync(db, $"""
                    BEGIN IMMEDIATE;
                    INSERT INTO Pair (Txn, Payload) VALUES ({txn}, '{payload}'), ({txn}, '{payload}');
                    UPDATE Counter SET Txns = Txns + 1;
                    COMMIT;
                    """);
            }
        }, Ct);

        long previous = -1;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(25, Ct);
            Assert.Equal(Snapshot, await DatabaseSnapshot.TakeAsync(Db, Ct));

            Assert.Equal("ok", await DatabaseSnapshot.IntegrityAsync(Snapshot, Ct));
            await using SqliteConnection copy = Open(Snapshot);
            long pairs = await ScalarAsync(copy, "SELECT count(*) FROM Pair");
            long txns  = await ScalarAsync(copy, "SELECT Txns FROM Counter");
            long odd   = await ScalarAsync(copy, "SELECT count(*) FROM (SELECT Txn FROM Pair GROUP BY Txn HAVING count(*) <> 2)");

            Assert.Equal(txns * 2, pairs);
            Assert.Equal(0, odd);
            Assert.True(txns >= previous, "a later snapshot is never older");
            // Self-contained: one file, not in WAL mode, nothing beside it needed.
            Assert.Equal("delete", (await ScalarTextAsync(copy, "PRAGMA journal_mode")).ToLowerInvariant());
            previous = txns;
        }

        await stop.CancelAsync();
        await writer;

        Assert.True(previous > 0, "the writer committed while snapshots were taken");
        Assert.True(Interlocked.Read(ref written) > previous, "the writer kept writing past the last snapshot");
        Assert.False(File.Exists(Snapshot + ".partial"));
    }

    [Fact]
    public async Task A_restore_that_brought_back_only_the_snapshot_starts_from_it()
    {
        await using (SqliteConnection db = Open(Db))
            await ExecAsync(db, "PRAGMA journal_mode = WAL; CREATE TABLE T (V TEXT); INSERT INTO T VALUES ('before the backup');");
        await DatabaseSnapshot.TakeAsync(Db, Ct);
        SqliteConnection.ClearAllPools();

        // What a Home Assistant restore leaves: /data as backed up, the live files excluded. A stray -wal must not be
        // replayed onto the snapshot.
        File.Delete(Db);
        File.Delete(Db + "-shm");
        await File.WriteAllTextAsync(Db + "-wal", "not this database's", Ct);

        Assert.True(DatabaseSnapshot.RestoreIfMissing(Db, NullLogger.Instance));

        Assert.False(File.Exists(Db + "-wal"));
        Assert.True(File.Exists(Snapshot), "the snapshot stays until the next backup replaces it");
        await using SqliteConnection restored = Open(Db);
        Assert.Equal("before the backup", await ScalarTextAsync(restored, "SELECT V FROM T"));
    }

    [Fact]
    public async Task A_live_database_is_never_replaced_by_the_snapshot()
    {
        await using (SqliteConnection db = Open(Db))
            await ExecAsync(db, "CREATE TABLE T (V TEXT); INSERT INTO T VALUES ('old');");
        await DatabaseSnapshot.TakeAsync(Db, Ct);
        await using (SqliteConnection db = Open(Db))
            await ExecAsync(db, "UPDATE T SET V = 'live';");

        Assert.False(DatabaseSnapshot.RestoreIfMissing(Db, NullLogger.Instance));

        await using SqliteConnection live = Open(Db);
        Assert.Equal("live", await ScalarTextAsync(live, "SELECT V FROM T"));
    }

    [Fact]
    public async Task No_database_is_an_error_not_an_empty_snapshot()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => DatabaseSnapshot.TakeAsync(Db, Ct));
        Assert.False(File.Exists(Snapshot));
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection c = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 30 }.ToString());
        c.Open();
        return c;
    }

    private static async Task ExecAsync(SqliteConnection db, string sql)
    {
        await using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqliteConnection db, string sql)
    {
        await using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection db, string sql)
    {
        await using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }
}
