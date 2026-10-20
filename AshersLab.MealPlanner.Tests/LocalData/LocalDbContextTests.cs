using AshersLab.MealPlanner.LocalData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AshersLab.MealPlanner.Tests.LocalData;

/// <summary>
/// The browser database's model and migrations, on ordinary SQLite. The provider differs from the browser's
/// (SqliteWasmBlazor) but the SQL dialect and the migrations are the same, which is what these check. Whether
/// the file survives a reload is a browser fact and is checked in the running app, not here.
/// </summary>
public sealed class LocalDbContextTests : IAsyncLifetime
{
    // An in-memory database lives as long as its first connection, so the tests hold one open.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        await using LocalDbContext db = CreateContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private LocalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LocalDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public void Every_model_change_has_a_migration()
    {
        // The browser applies migrations, never EnsureCreated. A model change with no migration builds and
        // then fails in every browser that already has the database.
        using LocalDbContext db = CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task First_start_creates_the_device()
    {
        DateTimeOffset now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        await using LocalDbContext db     = CreateContext();
        Device                     device = await DeviceRegistration.RecordOpenAsync(db, now, TestContext.Current.CancellationToken);

        Assert.Equal(7, device.Id.Version);
        Assert.Equal(now, device.FirstOpenedAt);
        Assert.Equal(now, device.LastOpenedAt);
        Assert.Equal(1, device.OpenCount);
    }

    [Fact]
    public async Task Later_starts_keep_the_id_and_count_up()
    {
        DateTimeOffset first  = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset second = first.AddHours(1);
        DateTimeOffset third  = first.AddDays(1);

        Guid id;
        await using (LocalDbContext db = CreateContext())
            id = (await DeviceRegistration.RecordOpenAsync(db, first, TestContext.Current.CancellationToken)).Id;
        await using (LocalDbContext db = CreateContext())
            await DeviceRegistration.RecordOpenAsync(db, second, TestContext.Current.CancellationToken);
        await using (LocalDbContext db = CreateContext())
            await DeviceRegistration.RecordOpenAsync(db, third, TestContext.Current.CancellationToken);

        await using LocalDbContext reader  = CreateContext();
        Device                     device  = Assert.Single(await reader.Devices.ToListAsync(TestContext.Current.CancellationToken));

        Assert.Equal(id, device.Id);
        Assert.Equal(first, device.FirstOpenedAt);
        Assert.Equal(third, device.LastOpenedAt);
        Assert.Equal(3, device.OpenCount);
    }
}
