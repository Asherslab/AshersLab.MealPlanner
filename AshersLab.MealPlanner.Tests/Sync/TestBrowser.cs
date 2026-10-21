using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Ingredients;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Ingredients;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>
/// One browser profile, minus the browser: its own migrated SQLite (in memory, plain provider), its device id,
/// and the same <see cref="LocalOps"/> and <see cref="SyncRound"/> the WASM app runs. <see cref="Online"/> off
/// stands in for DevTools' offline switch - rounds are simply not run.
/// </summary>
public sealed class TestBrowser : IDbContextFactory<LocalDbContext>, IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    /// <summary>Where rounds go. A test moves it to another host to stand in for the server being replaced or restored.</summary>
    public ISyncService Server { get; set; }

    public Guid DeviceId { get; }

    /// <summary>The wall clock this browser's edits are stamped with. Tests move it to order edits.</summary>
    public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private TestBrowser(ISyncService server, Guid deviceId)
    {
        Server   = server;
        DeviceId = deviceId;
        Contexts = new SerialLocalDbContextFactory(this);
    }

    /// <summary>One context at a time, as the app gets them. <see cref="CreateDbContext"/> is for the test's own reads.</summary>
    public SerialLocalDbContextFactory Contexts { get; }

    public static async Task<TestBrowser> OpenAsync(ISyncService server, Guid? deviceId = null)
    {
        TestBrowser browser = new(server, deviceId ?? Guid.CreateVersion7());
        await browser._connection.OpenAsync();
        await using LocalDbContext db = browser.CreateDbContext();
        await db.Database.MigrateAsync();
        return browser;
    }

    public LocalDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<LocalDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(YieldLikeTheWorker.Instance)
            .Options);

    /// <summary>
    /// In-memory SQLite finishes every "async" call synchronously, so overlapping work never actually interleaves
    /// and a bug that needs it cannot show. The browser's every statement is a round trip to a worker; this yields
    /// before each one so tests interleave the same way.
    /// </summary>
    private sealed class YieldLikeTheWorker : DbCommandInterceptor
    {
        public static readonly YieldLikeTheWorker Instance = new();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return result;
        }
    }

    public async Task<Guid> CreateIngredientAsync(string name)
    {
        Guid id = Guid.CreateVersion7(Now);
        await WriteAsync(id, OpKind.Create, name);
        return id;
    }

    public Task RenameAsync(Guid id, string name) => WriteAsync(id, OpKind.Update, [new OpField(IngredientRules.NameField, name)]);

    public Task DeleteAsync(Guid id) => WriteAsync(id, OpKind.Delete, []);

    private Task WriteAsync(Guid id, OpKind kind, IReadOnlyList<OpField> fields) =>
        WriteAsync(SyncedEntities.Ingredient, id, kind, fields);

    /// <summary>Any edit, as a page makes it.</summary>
    public async Task WriteAsync(SyncedEntity type, Guid id, OpKind kind, IReadOnlyList<OpField> fields)
    {
        await using LocalDbContext db = await Contexts.CreateDbContextAsync();
        await LocalOps.WriteAsync(db, DeviceId, Now, type, id, kind, fields);
    }

    /// <summary>Live stores only, as a page shows them.</summary>
    public async Task<Dictionary<Guid, string>> StoresAsync()
    {
        await using LocalDbContext db = CreateDbContext();
        return await db.Stores.Where(s => !s.Deleted).ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    private Task WriteAsync(Guid id, OpKind kind, string name) => WriteAsync(id, kind, [new OpField(IngredientRules.NameField, name)]);

    public Task<SyncRoundResult> SyncAsync(int pullBatch = SyncRound.PullBatch) =>
        SyncRound.RunAsync(Contexts, Server, DeviceId, pullBatch: pullBatch);

    /// <summary>Live ingredients only, as a page shows them.</summary>
    public async Task<Dictionary<Guid, string>> IngredientsAsync()
    {
        await using LocalDbContext db = CreateDbContext();
        return await db.Ingredients.Where(i => !i.Deleted).ToDictionaryAsync(i => i.Id, i => i.Name);
    }

    /// <summary>The ids of tombstones held here.</summary>
    public async Task<HashSet<Guid>> TombstonesAsync()
    {
        await using LocalDbContext db = CreateDbContext();
        return [.. await db.Ingredients.Where(i => i.Deleted).Select(i => i.Id).ToListAsync()];
    }

    public async Task<int> PendingAsync()
    {
        await using LocalDbContext db = CreateDbContext();
        return await db.Outbox.CountAsync();
    }

    public async Task<SyncState> StateAsync()
    {
        await using LocalDbContext db = CreateDbContext();
        return await db.SyncStates.AsNoTracking().SingleAsync();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
