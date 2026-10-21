using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.Grpc.Data.Models.Sync;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
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
        // nothing missing. (Two browsers giving back the same edit is Two_browsers_giving_back_the_same_edit_log_it_once.)
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
    public async Task Another_devices_edit_lost_to_a_restore_is_given_back_as_that_devices()
    {
        await using MealPlannerHost before = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        await using TestBrowser b = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();
        await b.SyncAsync();
        string backup = await before.BackupAsync();

        // B renames; A sees it. Then the Pi is restored, and B - a phone left in a drawer - never comes back.
        b.Now = b.Now.AddMinutes(1);
        await b.RenameAsync(flour, "plain flour");
        await b.SyncAsync();
        await a.SyncAsync();

        await using MealPlannerHost restored = MealPlannerHost.RestoredFrom(backup);
        a.Server = restored.CreateGrpcWebClient<ISyncService>();
        SyncRoundResult result = await a.SyncAsync();

        // The server checks that an op's clock was minted by the device that sends it, so B's edit has to go as
        // B's - sent as A's, it is refused and the rename is gone for good.
        Assert.Equal((1, 0), (result.PushedBack, result.Refused.Count));
        Assert.Equal("plain flour", (await ServerIngredientsAsync(restored))[flour]);
        Assert.Equal(b.DeviceId, (await ServerOpsAsync(restored)).Single(o => o.Kind == OpKind.Update).DeviceId);
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task Two_browsers_giving_back_the_same_edit_log_it_once()
    {
        await using MealPlannerHost before = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        await using TestBrowser b = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();
        string backup = await before.BackupAsync();

        a.Now = a.Now.AddMinutes(1);
        await a.RenameAsync(flour, "plain flour");
        await a.SyncAsync();
        await b.SyncAsync();

        // Restored. A queues the lost rename and loses the network before pushing it; B then queues and pushes its
        // own copy; A comes back and pushes its copy too.
        await using MealPlannerHost restored = MealPlannerHost.RestoredFrom(backup);
        HookedServer wireA = new(restored.CreateGrpcWebClient<ISyncService>()) { PushesBeforeOffline = 0 };
        a.Server = wireA;
        b.Server = restored.CreateGrpcWebClient<ISyncService>();
        await Assert.ThrowsAsync<RpcException>(() => a.SyncAsync());
        Assert.Equal(1, await a.PendingAsync());
        Assert.Equal(1, (await b.SyncAsync()).PushedBack);
        wireA.PushesBeforeOffline = null;
        await a.SyncAsync();

        // One op id for the one edit, whoever sends it: the rename is logged once, not once per browser.
        Assert.Equal(MealPlannerHost.SeedOps + 2, await OpCountAsync(restored));
        Assert.Equal("plain flour", (await ServerIngredientsAsync(restored))[flour]);
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task An_edit_still_queued_when_history_is_lost_goes_back_once()
    {
        await using MealPlannerHost before = new();
        await using TestBrowser a = await TestBrowser.OpenAsync(before.CreateGrpcWebClient<ISyncService>());
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();
        string backup = await before.BackupAsync();

        // Past the backup's head, so the restored server's log no longer holds A's cursor.
        a.Now = a.Now.AddMinutes(1);
        Guid eggs = await a.CreateIngredientAsync("eggs");
        await a.SyncAsync();

        // Restored; the planner renames while A's first round is between its push and its pull. The rename is in
        // the outbox and newer than anything the snapshot holds - it goes back by itself, not also as a recovered op.
        await using MealPlannerHost restored = MealPlannerHost.RestoredFrom(backup);
        HookedServer wire = new(restored.CreateGrpcWebClient<ISyncService>());
        wire.BeforePull = async () =>
        {
            wire.BeforePull = null;
            a.Now = a.Now.AddMinutes(1);
            await a.RenameAsync(flour, "plain flour");
        };
        a.Server = wire;
        SyncRoundResult result = await a.SyncAsync();

        Assert.Contains(wire.PullAnswers, p => p.Snapshot?.Reason == SnapshotReason.HistoryLost);
        Assert.Equal(1, result.PushedBack);     // the eggs only
        Assert.Equal(new Dictionary<Guid, string> { [flour] = "plain flour", [eggs] = "eggs" }, await ServerIngredientsAsync(restored));
        Assert.Equal(MealPlannerHost.SeedOps + 3, await OpCountAsync(restored));
        Assert.Equal(0, await a.PendingAsync());
    }

    // A new database (reinstalled without a backup) is ServerReplacedTests.

    private static async Task<List<DbOp>> ServerOpsAsync(MealPlannerHost host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.AsNoTracking().OrderBy(o => o.Seq).ToListAsync(Ct);
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
