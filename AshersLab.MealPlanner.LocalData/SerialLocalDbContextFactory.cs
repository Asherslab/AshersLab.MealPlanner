using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.LocalData;

/// <summary>
/// Hands out one <see cref="LocalDbContext"/> at a time: the next caller waits until the last context is disposed.
/// The only way into the browser database in the app (<c>LocalDatabase.Contexts</c>).
/// </summary>
/// <remarks>
/// <b>In the browser every context in a window shares one connection object</b> - SqliteWasmBlazor's, to one
/// SQLite handle in the worker. Two contexts alive at once break each other: a sync round's transaction and the
/// planner's edit fail with <c>cannot start a transaction within a transaction</c>, and a page re-reading while a
/// transaction is open closes the shared connection under it (<c>Connection must be Open</c>) or has its statement
/// join that transaction. Both were seen in Chrome with two quick adds. The cross-window shim
/// (<c>shared-database.js</c>) only queues <i>other</i> windows' statements; this queues this window's own.
/// <para>
/// So: keep a context for one short piece of work, never across a network call or a wait on the user, and never
/// open a second one while holding the first - that waits forever. <see cref="CreateDbContext"/> throws, because a
/// blocking wait cannot work on the browser's single thread.
/// </para>
/// </remarks>
public sealed class SerialLocalDbContextFactory(
    IDbContextFactory<LocalDbContext> inner
) : IDbContextFactory<LocalDbContext>
{
    private readonly SemaphoreSlim _one = new(1, 1);

    public LocalDbContext CreateDbContext() =>
        throw new NotSupportedException("Use CreateDbContextAsync: the browser database hands out one context at a time.");

    public async Task<LocalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        await _one.WaitAsync(cancellationToken);
        try
        {
            LocalDbContext db = await inner.CreateDbContextAsync(cancellationToken);
            db.ReleaseOnDispose(() => _one.Release());
            return db;
        }
        catch
        {
            _one.Release();
            throw;
        }
    }
}
