using AniTether.Api.Data;
using Xunit;

namespace AniTether.Api.Tests;

public class ListEntryMergeImportTests
{
    private static ListEntry NewEntry() => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        AnilistId = 1,
        Status = ListStatus.PlanToWatch,
    };

    [Fact]
    public void MergeImport_LocalProgressAhead_NoOp()
    {
        var entry = NewEntry();
        entry.ApplyProgress(10, totalEpisodes: 24);
        entry.Status = ListStatus.Watching;

        entry.MergeImport(isNewEntry: false, ListStatus.Dropped, 3, 7, null, null, 24);

        Assert.Equal(10, entry.Progress);
        Assert.Equal(ListStatus.Watching, entry.Status);
    }

    [Fact]
    public void MergeImport_ImportStrictlyAhead_AdoptsProgressAndStatus()
    {
        var entry = NewEntry();
        entry.ApplyProgress(3, totalEpisodes: 24);

        entry.MergeImport(isNewEntry: false, ListStatus.Completed, 24, 9, null, null, 24);

        Assert.Equal(24, entry.Progress);
        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.Equal(9, entry.Score);
    }

    [Fact]
    public void MergeImport_NewEntry_SeedsEverythingFromImport()
    {
        var entry = NewEntry();
        var started = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var finished = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

        entry.MergeImport(isNewEntry: true, ListStatus.Completed, 12, 8, started, finished, 12);

        Assert.Equal(12, entry.Progress);
        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.Equal(8, entry.Score);
        Assert.Equal(started, entry.StartedAt);
        Assert.Equal(finished, entry.FinishedAt);
    }

    [Fact]
    public void MergeImport_ExistingScoreAndDates_NotClobberedByBlankImport()
    {
        var entry = NewEntry();
        var started = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero);
        entry.ApplyProgress(1, totalEpisodes: 24);
        entry.StartedAt = started;
        entry.Score = 6;

        entry.MergeImport(isNewEntry: false, ListStatus.Watching, 5, null, null, null, 24);

        Assert.Equal(started, entry.StartedAt);
        Assert.Equal(6, entry.Score);
        Assert.Equal(5, entry.Progress);
    }

    [Fact]
    public void MergeImport_TieProgressOnExistingEntry_KeepsLocalStatus()
    {
        // A stale re-import (e.g. the source list hasn't caught up) must never regress
        // an already-Completed entry back to Watching/Dropped/etc just because the
        // episode counts happen to match.
        var entry = NewEntry();
        entry.ApplyProgress(24, totalEpisodes: 24); // -> Completed

        entry.MergeImport(isNewEntry: false, ListStatus.Watching, 24, 7, null, null, 24);

        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.Equal(7, entry.Score); // score still fills in (non-destructive), unlike status
    }

    [Fact]
    public void MergeImport_TieProgressOnNewEntry_StillAdoptsStatus()
    {
        // A brand-new entry has no local status to protect, so even a "0 == 0" tie
        // (e.g. importing a Dropped/Plan-to-Watch show with no progress) must still apply.
        var entry = NewEntry();

        entry.MergeImport(isNewEntry: true, ListStatus.Dropped, 0, null, null, null, 24);

        Assert.Equal(ListStatus.Dropped, entry.Status);
    }
}
