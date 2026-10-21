using AshersLab.MealPlanner.LocalData.Sync;
using AshersLab.MealPlanner.Shared.Contracts.Entities.Features.Sync;
using Grpc.Core;

namespace AshersLab.MealPlanner.Tests.Sync;

/// <summary>The WASM SyncLoop's decisions: what the chip says after a failure, and what starts the next round.</summary>
public class SyncScheduleTests
{
    private static readonly Guid Epoch = new("0199f0d2-0000-7000-8000-0000000000e1");

    [Theory]
    [MemberData(nameof(SyncFailureTests.Offline), MemberType = typeof(SyncFailureTests))]
    public void A_round_that_cannot_reach_the_server_is_offline(Exception exception) =>
        Assert.Equal(SyncConnection.Offline, SyncSchedule.AfterFailure(exception));

    [Theory]
    [MemberData(nameof(SyncFailureTests.Broken), MemberType = typeof(SyncFailureTests))]
    public void A_round_that_reached_the_server_and_still_failed_is_a_sync_problem(Exception exception) =>
        Assert.Equal(SyncConnection.Failed, SyncSchedule.AfterFailure(exception));

    [Fact]
    public void Only_the_protocol_refusal_is_refused()
    {
        Assert.Equal(SyncConnection.Refused,
            SyncSchedule.AfterFailure(new RpcException(new Status(StatusCode.FailedPrecondition, SyncProtocol.RefusedStatus))));
        Assert.Equal(SyncConnection.Failed,
            SyncSchedule.AfterFailure(new RpcException(new Status(StatusCode.FailedPrecondition, "something else"))));
    }

    [Fact]
    public void Offline_retries_quickly_and_everything_else_waits_the_fallback()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), SyncSchedule.Wait(SyncConnection.Offline));
        foreach (SyncConnection connection in Enum.GetValues<SyncConnection>().Where(c => c != SyncConnection.Offline))
            Assert.Equal(TimeSpan.FromSeconds(30), SyncSchedule.Wait(connection));
    }

    [Fact]
    public void Online_a_notice_starts_a_round_only_when_the_server_has_moved()
    {
        Assert.False(SyncSchedule.KickOnNotice(SyncConnection.Online, Epoch, 40, Epoch, 40));    // the heartbeat
        Assert.True(SyncSchedule.KickOnNotice(SyncConnection.Online, Epoch, 40, Epoch, 41));     // someone's edit
        Assert.True(SyncSchedule.KickOnNotice(SyncConnection.Online, Epoch, 40, Epoch, 30));     // restored from a backup
        Assert.True(SyncSchedule.KickOnNotice(SyncConnection.Online, Epoch, 40, Guid.NewGuid(), 40));   // a new database
    }

    [Theory]
    [InlineData(SyncConnection.Offline)]
    [InlineData(SyncConnection.Connecting)]
    public void While_unreachable_any_notice_starts_a_round(SyncConnection connection) =>
        Assert.True(SyncSchedule.KickOnNotice(connection, Epoch, 40, Epoch, 40));

    [Fact]
    public void A_failing_round_is_not_rerun_by_notices_heartbeats_or_other_devices_edits()
    {
        Assert.False(SyncSchedule.KickOnNotice(SyncConnection.Failed, Epoch, 40, Epoch, 40));
        Assert.False(SyncSchedule.KickOnNotice(SyncConnection.Failed, Epoch, 40, Epoch, 41));
    }

    [Theory]
    [InlineData(SyncConnection.Online)]
    [InlineData(SyncConnection.Connecting)]
    [InlineData(SyncConnection.Offline)]
    public void A_dropped_stream_shows_offline(SyncConnection connection) =>
        Assert.Equal(SyncConnection.Offline, SyncSchedule.AfterStreamDropped(connection));

    [Theory]
    [InlineData(SyncConnection.Failed)]
    [InlineData(SyncConnection.Refused)]
    [InlineData(SyncConnection.WaitingForChoice)]
    public void A_dropped_stream_never_hides_a_problem_or_an_open_question_behind_offline(SyncConnection connection) =>
        Assert.Null(SyncSchedule.AfterStreamDropped(connection));
}
