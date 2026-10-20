using AshersLab.MealPlanner.Grpc.Data;
using AshersLab.MealPlanner.LocalData;
using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Requests.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Messages.Responses.Features.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Services.Features.Sync;
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
        await a.RenameAsync(flour, "plain flour");
        await a.SyncAsync();

        await using TestBrowser fresh = await TestBrowser.OpenAsync(_server);
        fresh.Now = a.Now.AddMinutes(1);
        Guid oil = await fresh.CreateIngredientAsync("olive oil");

        await fresh.SyncAsync();

        Dictionary<Guid, string> seen = await fresh.IngredientsAsync();
        Assert.Equal("plain flour", seen[flour]);
        Assert.Equal("olive oil", seen[oil]);
        Assert.Equal(0, await fresh.PendingAsync());
        Assert.True((await fresh.StateAsync()).PulledThroughSeq >= 3);
    }

    [Fact]
    public async Task A_push_whose_answer_was_lost_is_not_applied_twice()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        await a.CreateIngredientAsync("bagels");

        List<OutboxOp> pending;
        await using (LocalDbContext db = a.CreateDbContext())
            pending = await LocalOps.PendingAsync(db, 10, Ct);
        PushRequest request = new()
        {
            ProtocolVersion = SyncProtocol.Current,
            Ops =
            [
                .. pending.Select(o => new SyncOp
                {
                    OpId = o.OpId, DeviceId = a.DeviceId, Hlc = o.Hlc, Entity = o.Entity, EntityId = o.EntityId, Kind = o.Kind,
                    Fields = [new SyncField { Name = "Name", Value = "bagels" }],
                }),
            ],
        };

        PushResponse first = await _server.Push(request, Ct);
        PushResponse retry = await _server.Push(request, Ct);

        Assert.Equal(PushOutcome.Accepted, Assert.Single(first.Results).Outcome);
        Assert.Equal(PushOutcome.Duplicate, Assert.Single(retry.Results).Outcome);
        Assert.Equal(first.Results[0].Seq, retry.Results[0].Seq);

        // The outbox still holds it (the "answer" was lost); the next round pushes it again, harmlessly, and the
        // pull confirms it.
        await a.SyncAsync();
        Assert.Equal(0, await a.PendingAsync());

        using IServiceScope scope = _host.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.CountAsync(Ct));
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

        using IServiceScope scope = _host.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<MealPlannerDbContext>().Ops.CountAsync(Ct));
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
        Assert.Equal(0, stream.Current.LatestSeq);

        await a.CreateIngredientAsync("tortillas");
        await a.SyncAsync();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(1, stream.Current.LatestSeq);
    }

    [Fact]
    public async Task A_cursor_from_another_server_database_gets_a_snapshot_and_keeps_queued_edits()
    {
        await using TestBrowser a = await TestBrowser.OpenAsync(_server);
        Guid old = await a.CreateIngredientAsync("from the old server");
        await a.SyncAsync();

        // Pretend the server's database was replaced: the browser's cursor names an epoch this server never had.
        await using (LocalDbContext db = a.CreateDbContext())
        {
            SyncState state = await db.SyncStates.SingleAsync(Ct);
            state.ServerEpoch = Guid.CreateVersion7();
            await db.SaveChangesAsync(Ct);
        }

        // Still offline from the "new" server's point of view: one edit queued.
        a.Now = a.Now.AddMinutes(1);
        Guid queued = await a.CreateIngredientAsync("made while away");

        await a.SyncAsync();

        Dictionary<Guid, string> seen = await a.IngredientsAsync();
        Assert.Equal("made while away", seen[queued]);
        // This test's server is the same database, so the old row comes back in the snapshot; what matters is
        // that the queued edit survived the reset and reached the server.
        Assert.Equal("from the old server", seen[old]);
        Assert.Equal("made while away", (await ServerIngredientsAsync())[queued]);
    }
}
