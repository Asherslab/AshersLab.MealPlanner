using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>
/// The server's database going away under the browsers: restored from an older Home Assistant backup (same
/// database, shorter log), or replaced by a new one. Each is two real hosts - the second started on the first's
/// backup, or empty - with the browsers moved across.
/// </summary>
public sealed class SyncRecoveryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]     // nothing written after the restore: the browsers' cursors are past the end of the log
    [InlineData(true)]      // another browser writes first: the same seqs now name different ops
    public async Task A_server_restored_from_an_older_backup_gets_back_what_the_browsers_hold(bool someoneWritesFirst)
    {
        await using MealPlannerHost before = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        await using TestBrowser b = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());

        Guid flour = await a.CreateIngredientAsync("flour");
        Guid sugar = await a.CreateIngredientAsync("sugar");
        await a.SyncAsync();
        await b.SyncAsync();

        string backup = await before.BackupAsync();

        // After the backup: a create, a rename, a delete - the three seqs after the seeds and those two - and everyone
        // sees them.
        a.Now = a.Now.AddMinutes(1);
        Guid eggs = await a.CreateIngredientAsync("eggs");
        await a.RenameAsync(flour, "plain flour");
        await a.SyncAsync();
        b.Now = a.Now.AddMinutes(1);
        await b.DeleteAsync(sugar);
        await b.SyncAsync();
        await a.SyncAsync();
        Assert.Equal(MealPlannerHost.SeedOps + 5, (await a.StateAsync()).PulledThroughSeq);

        // The Pi is restored. Same database, same epoch, a log that ends at the two creates.
        await using MealPlannerHost restored = MealPlannerHost.RestoredFrom(backup);
        HookedServer wireA = new(restored.CreateGrpcWebClient<ISyncService>());
        a.Server = wireA;
        b.Server = restored.CreateGrpcWebClient<ISyncService>();
        await using TestBrowser c = await TestBrowser.OpenAsync(restored.CreateGrpcWebClient<ISyncService>());
        c.Now = b.Now.AddMinutes(1);

        List<Guid> written = [];
        if (someoneWritesFirst)
        {
            foreach (string name in new[] { "milk", "butter", "rice" })
                written.Add(await c.CreateIngredientAsync(name));
            await c.SyncAsync();
        }

        await a.SyncAsync();
        await b.SyncAsync();
        await c.SyncAsync();
        await a.SyncAsync();
        await b.SyncAsync();

        Assert.Contains(wireA.PullAnswers, p => p.Snapshot?.Reason == SnapshotReason.HistoryLost);

        Dictionary<Guid, string> expected = new() { [flour] = "plain flour", [eggs] = "eggs" };
        foreach ((Guid id, string name) in written.Zip(["milk", "butter", "rice"]))
            expected[id] = name;

        Assert.Equal(expected, await ServerIngredientsAsync(restored));
        foreach (TestBrowser browser in new[] { a, b, c })
        {
            Assert.Equal(expected, await browser.IngredientsAsync());
            Assert.Contains(sugar, await browser.TombstonesAsync());
            Assert.Equal(0, await browser.PendingAsync());
        }

        // A pushed back the create, the rename and (as a new delete) the delete. B, recovering after A, found
        // nothing missing; its copies of A's edits would have carried the same op ids anyway.
        Assert.Equal(MealPlannerHost.SeedOps + written.Count + 2 + 3, await OpCountAsync(restored));
        await c.SyncAsync();
        foreach (TestBrowser browser in new[] { a, b, c })
            Assert.Equal(await OpCountAsync(restored), (await browser.StateAsync()).PulledThroughSeq);
    }

    [Fact]
    public async Task An_edit_accepted_just_before_a_restore_is_pushed_again()
    {
        await using MealPlannerHost before = new();
        HookedServer wire = new(before.CreateGrpcWebClient<ISyncService>());
        await using TestBrowser a = await TestBrowser.OpenAsync(wire);
        await a.SyncAsync();
        string backup = await before.BackupAsync();

        // Pushed and accepted; the network goes before the pull brings it back.
        Guid wraps = await a.CreateIngredientAsync("wraps");
        wire.BeforePull = () => throw new RpcException(new Status(StatusCode.Unavailable, "gone"));
        await Assert.ThrowsAsync<RpcException>(() => a.SyncAsync());
        Assert.Equal("wraps", (await ServerIngredientsAsync(before))[wraps]);

        // Restored to before it. The browser's cursor (seq 0) is still a point in this log, so nothing looks
        // wrong - it is the op still in the outbox that puts it back.
        await using MealPlannerHost restored = MealPlannerHost.RestoredFrom(backup);
        a.Server = restored.CreateGrpcWebClient<ISyncService>();
        await a.SyncAsync();

        Assert.Equal("wraps", (await ServerIngredientsAsync(restored))[wraps]);
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task A_new_server_database_forgets_the_old_rows_and_keeps_queued_edits()
    {
        await using MealPlannerHost old = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(old.CreateGrpcWebClient<ISyncService>());
        Guid gone = await a.CreateIngredientAsync("from the old server");
        await a.SyncAsync();

        // A wiped server: a new database, a new epoch. The edit is made while the round is between its push and
        // its pull, so it is only in the outbox when the snapshot arrives; then the network goes.
        await using MealPlannerHost fresh = new();
        HookedServer wire = new(fresh.CreateGrpcWebClient<ISyncService>());
        a.Server = wire;
        a.Now = a.Now.AddMinutes(1);
        Guid queued = Guid.Empty;
        wire.BeforePull = async () =>
        {
            wire.BeforePull = null;
            wire.PushesBeforeOffline = 0;
            queued = await a.CreateIngredientAsync("made while away");
        };

        await Assert.ThrowsAsync<RpcException>(() => a.SyncAsync());

        Assert.Equal(SnapshotReason.NewDatabase, Assert.Single(wire.PullAnswers).Snapshot?.Reason);
        Assert.Equal(new Dictionary<Guid, string> { [queued] = "made while away" }, await a.IngredientsAsync());
        Assert.Equal(1, await a.PendingAsync());

        wire.PushesBeforeOffline = null;
        await a.SyncAsync();

        Assert.Equal(new Dictionary<Guid, string> { [queued] = "made while away" }, await ServerIngredientsAsync(fresh));
        Assert.DoesNotContain(gone, (await a.IngredientsAsync()).Keys);
        Assert.Equal(0, await a.PendingAsync());
    }

    private static async Task<Dictionary<Guid, string>> ServerIngredientsAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
        return await db.Ingredients.Where(i => !i.Deleted).ToDictionaryAsync(i => i.Id, i => i.Name, Ct);
    }

    private static async Task<int> OpCountAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.CountAsync(Ct);
    }
}
