using AniTether.Api.Data;
using Xunit;

namespace AniTether.Api.Tests;

public class ListEntryTests
{
    private static ListEntry NewEntry() => new()
    {
        Id = Guid.NewGuid(),
        UserId = "user-1",
        AnilistId = 1,
        Status = ListStatus.PlanToWatch,
    };

    [Fact]
    public void ApplyProgress_MovesPlanToWatch_ToWatching()
    {
        var entry = NewEntry();

        entry.ApplyProgress(1, totalEpisodes: 24);

        Assert.Equal(ListStatus.Watching, entry.Status);
        Assert.Equal(1, entry.Progress);
    }

    [Fact]
    public void ApplyProgress_NeverLowersProgress()
    {
        var entry = NewEntry();
        entry.ApplyProgress(5, totalEpisodes: 24);

        entry.ApplyProgress(3, totalEpisodes: 24);

        Assert.Equal(5, entry.Progress);
    }

    [Fact]
    public void ApplyProgress_AtTotalEpisodes_CompletesAndSetsFinishedAt()
    {
        var entry = NewEntry();

        entry.ApplyProgress(24, totalEpisodes: 24);

        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.NotNull(entry.FinishedAt);
    }

    [Fact]
    public void ApplyProgress_EpisodeBeyondTotal_ClampsInsteadOfRejecting()
    {
        var entry = NewEntry();

        entry.ApplyProgress(26, totalEpisodes: 24);

        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.Equal(26, entry.Progress);
    }

    [Fact]
    public void ApplyProgress_UnknownEpisodeCount_NeverAutoCompletes()
    {
        var entry = NewEntry();

        entry.ApplyProgress(999, totalEpisodes: null);

        Assert.Equal(ListStatus.Watching, entry.Status);
        Assert.Null(entry.FinishedAt);
    }

    [Fact]
    public void SetProgress_Lower_ReopensCompletedAsWatching()
    {
        var entry = NewEntry();
        entry.ApplyProgress(24, totalEpisodes: 24);

        entry.SetProgress(20, totalEpisodes: 24);

        Assert.Equal(20, entry.Progress);
        Assert.Equal(ListStatus.Watching, entry.Status);
        Assert.Null(entry.FinishedAt);
    }

    [Fact]
    public void SetProgress_Lower_KeepsNonCompletedStatus()
    {
        var entry = NewEntry();
        entry.ApplyProgress(5, totalEpisodes: 24);
        entry.Status = ListStatus.OnHold;

        entry.SetProgress(3, totalEpisodes: 24);

        Assert.Equal(3, entry.Progress);
        Assert.Equal(ListStatus.OnHold, entry.Status);
    }

    [Fact]
    public void SetProgress_Higher_BehavesLikeApplyProgress()
    {
        var entry = NewEntry();

        entry.SetProgress(24, totalEpisodes: 24);

        Assert.Equal(ListStatus.Completed, entry.Status);
        Assert.NotNull(entry.FinishedAt);
    }

    [Fact]
    public void SetProgress_ToZero_Allowed()
    {
        var entry = NewEntry();
        entry.ApplyProgress(1, totalEpisodes: 24);

        entry.SetProgress(0, totalEpisodes: 24);

        Assert.Equal(0, entry.Progress);
    }

    [Fact]
    public void MergeImport_OutOfRangeValues_AreClampedOrDropped()
    {
        var entry = NewEntry();

        entry.MergeImport(isNewEntry: true, ListStatus.Watching, int.MaxValue, importedScore: 85, null, null, totalEpisodes: null);

        Assert.Equal(ListEntry.MaxEpisode, entry.Progress);
        Assert.Null(entry.Score);
    }
}
