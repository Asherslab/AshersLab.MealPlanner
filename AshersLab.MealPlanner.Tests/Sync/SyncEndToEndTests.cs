using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
using AshersLab.MealPlanner.Shared.Data.Sync;
using AshersLab.MealPlanner.Shared.Domain.Sync;
using AshersLab.MealPlanner.Tests.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>
/// Two browsers' databases syncing through the real host over gRPC-Web, with the browser's own round code. The
/// cases the slice has to prove in the running app, proven here first.
/// </summary>
public sealed class SyncEndToEndTests : IAsyncLifetime
{
    private readonly MealPlannerHost _host = new();
    private ISyncService _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = _host.CreateGrpcWebClient<ISyncService>();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Dictionary<Guid, string>> ServerIngredientsAsync()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
        return await db.Ingredients.Where(i => !i.Deleted).ToDictionaryAsync(i => i.Id, i => i.Name, Ct);
    }

    private async Task<HashSet<Guid>> ServerTombstonesAsync()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        MealPlannerDbContext db = scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>();
        return [.. await db.Ingredients.Where(i => i.Deleted).Select(i => i.Id).ToListAsync(Ct)];
    }

    /// <summary>Ops in the server's log after the seeds - the ones the tests' browsers wrote.</summary>
    private async Task<int> OpCountAsync()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.CountAsync(Ct) - MealPlannerHost.SeedOps;
    }

    [Fact]
    public async Task A_create_on_one_browser_reaches_the_other()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        Guid mince = await a.CreateIngredientAsync("beef mince");
        Assert.Equal(1, await a.PendingAsync());

        await a.SyncAsync();
        Assert.Equal(0, await a.PendingAsync());

        SyncRoundResult pulled = await b.SyncAsync();

        Assert.True(pulled.Changed);
        Assert.Equal("beef mince", (await b.IngredientsAsync())[mince]);
        Assert.Equal("beef mince", (await ServerIngredientsAsync())[mince]);
    }

    [Fact]
    public async Task Overlapping_writes_and_a_round_in_one_window_do_not_collide()
    {
        // In the browser every context in a window shares one connection, as TestBrowser's do. Without
        // SerialLocalDbContextFactory two of these begin a transaction inside the other's and fail.
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);

        Task[] work =
        [
            .. Enumerable.Range(0, 10).Select(i => a.CreateIngredientAsync($"item {i}")),
            a.SyncAsync(),
            a.SyncAsync(),
        ];
        await Task.WhenAll(work);
        await a.SyncAsync();

        Assert.Equal(10, (await a.IngredientsAsync()).Count);
        Assert.Equal(10, (await ServerIngredientsAsync()).Count);
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task An_offline_rename_syncs_on_reconnect()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        Guid cheese = await a.CreateIngredientAsync("cheese");
        await a.SyncAsync();
        await b.SyncAsync();

        // A goes offline: the rename applies locally and waits in the outbox.
        a.Now = a.Now.AddMinutes(5);
        await a.RenameAsync(cheese, "grated cheese");
        Assert.Equal("grated cheese", (await a.IngredientsAsync())[cheese]);
        Assert.Equal(1, await a.PendingAsync());
        Assert.Equal("cheese", (await ServerIngredientsAsync())[cheese]);

        // Back online.
        await a.SyncAsync();
        await b.SyncAsync();

        Assert.Equal(0, await a.PendingAsync());
        Assert.Equal("grated cheese", (await b.IngredientsAsync())[cheese]);
        Assert.Equal("grated cheese", (await ServerIngredientsAsync())[cheese]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_renames_resolve_to_the_later_edit_whoever_syncs_first(bool laterSyncsFirst)
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        Guid eggs = await a.CreateIngredientAsync("eggs");
        await a.SyncAsync();
        await b.SyncAsync();

        // Both offline, both rename. B's edit is a minute later.
        a.Now = a.Now.AddMinutes(10);
        b.Now = a.Now.AddMinutes(1);
        await a.RenameAsync(eggs, "free-range eggs");
        await b.RenameAsync(eggs, "eggs (dozen)");

        TestBrowser first  = laterSyncsFirst ? b : a;
        TestBrowser second = laterSyncsFirst ? a : b;
        await first.SyncAsync();
        await second.SyncAsync();
        await first.SyncAsync();

        Assert.Equal("eggs (dozen)", (await a.IngredientsAsync())[eggs]);
        Assert.Equal("eggs (dozen)", (await b.IngredientsAsync())[eggs]);
        Assert.Equal("eggs (dozen)", (await ServerIngredientsAsync())[eggs]);
    }

    [Fact]
    public async Task An_edit_after_seeing_a_fast_clock_still_wins()
    {
        await using TestBrowser fast = await TestBrowser.OpenAsync(_server);
        await using TestBrowser slow = await TestBrowser.OpenAsync(_server);

        fast.Now = slow.Now.AddMinutes(10);
        Guid rice = await fast.CreateIngredientAsync("rice");
        await fast.SyncAsync();
        await slow.SyncAsync();

        // The slow device renames after seeing the fast one's create: its wall clock is behind, its clock is not.
        await slow.RenameAsync(rice, "jasmine rice");
        await slow.SyncAsync();
        await fast.SyncAsync();

        Assert.Equal("jasmine rice", (await fast.IngredientsAsync())[rice]);
        Assert.Equal("jasmine rice", (await ServerIngredientsAsync())[rice]);
    }

    [Fact]
    public async Task A_new_browser_starts_from_a_snapshot_and_keeps_its_own_unsynced_edits()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Guid flour = await a.CreateIngredientAsync("flour");
        await a.SyncAsync();

        // The fresh browser's edits land while its first round is between push and pull - so they are queued and
        // not on the server when the snapshot arrives, and the second push of the round is offline. What it shows
        // afterwards is what the snapshot merge alone made of them.
        HookedServer wire = new(_server);
        await using TestBrowser fresh = await TestBrowser.OpenAsync(wire);
        fresh.Now = a.Now.AddMinutes(1);
        Guid oil = Guid.Empty;
        wire.BeforePull = async () =>
        {
            wire.BeforePull = null;
            wire.PushesBeforeOffline = 0;
            oil = await fresh.CreateIngredientAsync("olive oil");
            await fresh.RenameAsync(flour, "plain flour");      // newer than the snapshot's "flour"
        };

        await Assert.ThrowsAsync<RpcException>(() => fresh.SyncAsync());

        Assert.Equal(SnapshotReason.FirstLoad, Assert.Single(wire.PullAnswers).Snapshot?.Reason);
        Dictionary<Guid, string> seen = await fresh.IngredientsAsync();
        Assert.Equal("plain flour", seen[flour]);
        Assert.Equal("olive oil", seen[oil]);
        Assert.Equal(2, await fresh.PendingAsync());
        Assert.Equal("flour", (await ServerIngredientsAsync())[flour]);

        // Back online: both go up and come back.
        wire.PushesBeforeOffline = null;
        await fresh.SyncAsync();
        await a.SyncAsync();

        Assert.Equal(0, await fresh.PendingAsync());
        Assert.Equal("plain flour", (await a.IngredientsAsync())[flour]);
        Assert.Equal("olive oil", (await ServerIngredientsAsync())[oil]);
    }

    [Fact]
    public async Task A_push_whose_answer_was_lost_stays_queued_and_is_confirmed_by_the_pull()
    {
        HookedServer wire = new(_server);
        await using TestBrowser a = await TestBrowser.OpenAsync(wire);
        await a.SyncAsync();     // a cursor: otherwise the next pull is a first-load snapshot, a different path
        await a.CreateIngredientAsync("bagels");

        // The server logs it; the answer never arrives.
        wire.LoseNextPushAnswer = true;
        await Assert.ThrowsAsync<RpcException>(() => a.SyncAsync());
        Assert.Equal(1, await OpCountAsync());
        Assert.Equal(1, await a.PendingAsync());

        // The next round pushes it again. The server answers "already have it" at a seq this browser has not
        // pulled to, so it stays queued until the pull brings it back.
        int pendingAtPull = -1;
        wire.BeforePull = async () => pendingAtPull = await a.PendingAsync();
        wire.PullAnswers.Clear();
        await a.SyncAsync();

        Assert.All(wire.PullAnswers, p => Assert.Null(p.Snapshot));
        Assert.Equal(PushOutcome.Duplicate, Assert.Single(Assert.Single(wire.PushAnswers).Results).Outcome);
        Assert.Equal(1, pendingAtPull);
        Assert.Equal(0, await a.PendingAsync());
        Assert.Equal(1, await OpCountAsync());
    }

    [Fact]
    public async Task An_accepted_op_stays_queued_until_the_pull_brings_it_back()
    {
        HookedServer wire = new(_server);
        await using TestBrowser a = await TestBrowser.OpenAsync(wire);
        await a.SyncAsync();
        await a.CreateIngredientAsync("wraps");

        int pendingAtPull = -1;
        wire.BeforePull = async () => pendingAtPull = await a.PendingAsync();
        await a.SyncAsync();

        Assert.Equal(PushOutcome.Accepted, Assert.Single(Assert.Single(wire.PushAnswers).Results).Outcome);
        Assert.Equal(1, pendingAtPull);
        Assert.Equal(0, await a.PendingAsync());
    }

    [Fact]
    public async Task A_delete_reaches_the_other_browser_and_a_later_offline_rename_cannot_bring_it_back()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        Guid avocado = await a.CreateIngredientAsync("avocado");
        await a.SyncAsync();
        await b.SyncAsync();

        // A deletes; B, offline and not having seen it, renames a minute later.
        a.Now = a.Now.AddMinutes(1);
        b.Now = a.Now.AddMinutes(1);
        await a.DeleteAsync(avocado);
        await b.RenameAsync(avocado, "avocados");
        Assert.DoesNotContain(avocado, (await a.IngredientsAsync()).Keys);

        await a.SyncAsync();
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.DoesNotContain(avocado, (await a.IngredientsAsync()).Keys);
        Assert.DoesNotContain(avocado, (await b.IngredientsAsync()).Keys);
        Assert.DoesNotContain(avocado, (await ServerIngredientsAsync()).Keys);
        Assert.Contains(avocado, await b.TombstonesAsync());
        Assert.Contains(avocado, await ServerTombstonesAsync());
        Assert.Equal(0, await b.PendingAsync());

        // A browser that arrives later gets the tombstone in its snapshot, not the ingredient.
        await using TestBrowser fresh = await TestBrowser.OpenAsync(_server);
        await fresh.SyncAsync();
        Assert.Empty(await fresh.IngredientsAsync());
        Assert.Contains(avocado, await fresh.TombstonesAsync());
    }

    [Fact]
    public async Task A_delete_of_an_id_never_seen_leaves_a_tombstone_that_a_later_edit_cannot_fill()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(_server);

        // The delete arrives first - as it would from a device whose create is still in an outbox somewhere.
        Guid id = Guid.CreateVersion7();
        await a.DeleteAsync(id);
        await a.SyncAsync();
        await b.SyncAsync();

        b.Now = b.Now.AddMinutes(1);
        await b.RenameAsync(id, "ghost");
        await b.SyncAsync();
        await a.SyncAsync();

        Assert.Contains(id, await a.TombstonesAsync());
        Assert.Contains(id, await b.TombstonesAsync());
        Assert.Empty(await b.IngredientsAsync());
        Assert.Contains(id, await ServerTombstonesAsync());
        Assert.DoesNotContain(id, (await ServerIngredientsAsync()).Keys);
    }

    [Fact]
    public async Task Pulls_more_than_one_page_in_one_round()
    {
        HookedServer wire = new(_server);
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await using TestBrowser b = await TestBrowser.OpenAsync(wire);
        await b.SyncAsync();     // a cursor, so what follows is ops, not a snapshot
        wire.PullAnswers.Clear();

        for (int i = 0; i < 7; i++)
            await a.CreateIngredientAsync($"spice {i}");
        await a.SyncAsync();

        await b.SyncAsync(pullBatch: 3);

        Assert.Equal(7, (await b.IngredientsAsync()).Count);
        Assert.Equal([3, 3, 1], wire.PullAnswers.Select(p => p.Ops.Count));
        Assert.Equal([true, true, false], wire.PullAnswers.Select(p => p.HasMore));
        Assert.Equal(MealPlannerHost.SeedOps + 7, (await b.StateAsync()).PulledThroughSeq);
    }

    [Fact]
    public async Task Refuses_an_invalid_op_without_logging_it()
    {
        Guid device = Guid.CreateVersion7();
        PushResponse response = await _server.Push(new PushRequest
        {
            ProtocolVersion = SyncProtocol.Current,
            Ops =
            [
                new SyncOp
                {
                    OpId = Guid.CreateVersion7(), DeviceId = device, Hlc = new Hlc(1, 0, device).ToString(),
                    Entity = "Ingredient", EntityId = Guid.CreateVersion7(), Kind = OpKind.Create,
                    Fields = [new SyncField { Name = "Name", Value = "" }],
                },
                new SyncOp
                {
                    OpId = Guid.CreateVersion7(), DeviceId = device, Hlc = new Hlc(1, 1, device).ToString(),
                    Entity = "Spaceship", EntityId = Guid.CreateVersion7(), Kind = OpKind.Create,
                },
                new SyncOp
                {
                    OpId = Guid.CreateVersion7(), DeviceId = device, Hlc = new Hlc(1, 2, Guid.CreateVersion7()).ToString(),
                    Entity = "Ingredient", EntityId = Guid.CreateVersion7(), Kind = OpKind.Create,
                    Fields = [new SyncField { Name = "Name", Value = "someone else's clock" }],
                },
            ],
        }, Ct);

        Assert.All(response.Results, r => Assert.Equal(PushOutcome.Rejected, r.Outcome));
        Assert.All(response.Results, r => Assert.False(string.IsNullOrEmpty(r.Reason)));

        Assert.Equal(0, await OpCountAsync());
    }

    [Theory]
    [InlineData(SyncProtocol.OldestSupported - 1)]
    [InlineData(SyncProtocol.Current + 1)]
    public async Task Refuses_a_client_on_a_protocol_version_it_cannot_read(int version)
    {
        RpcException push = await Assert.ThrowsAsync<RpcException>(() =>
            _server.Push(new PushRequest { ProtocolVersion = version }, Ct));
        RpcException pull = await Assert.ThrowsAsync<RpcException>(() =>
            _server.Pull(new PullRequest { ProtocolVersion = version }, Ct));
        RpcException changes = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (ChangesNotice _ in _server.Changes(new ChangesRequest { ProtocolVersion = version }, Ct))
            {
            }
        });

        foreach (RpcException refused in new[] { push, pull, changes })
        {
            Assert.Equal(StatusCode.FailedPrecondition, refused.StatusCode);
            Assert.Equal(SyncProtocol.RefusedStatus, refused.Status.Detail);
        }
    }

    [Fact]
    public async Task The_change_stream_announces_a_push()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(20));

        await using IAsyncEnumerator<ChangesNotice> stream =
            _server.Changes(new ChangesRequest { ProtocolVersion = SyncProtocol.Current }, stop.Token).GetAsyncEnumerator(stop.Token);

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(MealPlannerHost.SeedOps, stream.Current.LatestSeq);

        await a.CreateIngredientAsync("tortillas");
        await a.SyncAsync();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(MealPlannerHost.SeedOps + 1, stream.Current.LatestSeq);
    }

    /// <summary>
    /// A rejected op stays applied in the browser that made it, so the browser must refuse, before applying, every
    /// op the server would reject. This is the guard: each case goes through LocalOps and, as a hand-built op with a
    /// valid clock, straight to the server - and the two must agree.
    /// </summary>
    [Theory]
    [InlineData(OpKind.Create, "Name", "beef mince", false)]
    [InlineData(OpKind.Create, "Name", "", true)]
    [InlineData(OpKind.Create, "Name", " untrimmed ", true)]
    [InlineData(OpKind.Create, "Colour", "red", true)]
    [InlineData(OpKind.Update, "Name", "x", false)]
    [InlineData(OpKind.Update, null, null, true)]
    [InlineData(OpKind.Delete, null, null, false)]
    [InlineData(OpKind.Delete, "Name", "x", true)]
    [InlineData((OpKind)7, "Name", "x", true)]
    public async Task The_browser_refuses_exactly_what_the_server_would(OpKind kind, string? field, string? value, bool invalid)
    {
        List<OpField> fields = field is null ? [] : [new OpField(field, value)];
        Guid entityId = Guid.CreateVersion7();

        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        bool refusedHere;
        try
        {
            await using LocalDbContext db = await a.Contexts.CreateDbContextAsync(Ct);
            await LocalOps.WriteAsync(db, a.DeviceId, a.Now, SyncedEntities.Ingredient, entityId, kind, fields, Ct);
            refusedHere = false;
        }
        catch (ArgumentException)
        {
            refusedHere = true;
        }

        Guid device = Guid.CreateVersion7();
        PushResponse direct = await _server.Push(new PushRequest
        {
            ProtocolVersion = SyncProtocol.Current,
            Ops =
            [
                new SyncOp
                {
                    OpId = Guid.CreateVersion7(), DeviceId = device, Hlc = new Hlc(1, 0, device).ToString(),
                    Entity = "Ingredient", EntityId = entityId, Kind = kind,
                    Fields = [.. fields.Select(f => new SyncField { Name = f.Name, Value = f.Value })],
                },
            ],
        }, Ct);

        Assert.Equal(invalid, refusedHere);
        Assert.Equal(invalid, Assert.Single(direct.Results).Outcome == PushOutcome.Rejected);
        if (refusedHere)
        {
            Assert.Equal(0, await a.PendingAsync());
            Assert.Empty(await a.IngredientsAsync());
            Assert.Empty(await a.TombstonesAsync());
        }
    }
}
