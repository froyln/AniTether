namespace AniTether.Api.Data;

public class ListEntry
{
    public const int MaxEpisode = 10000;
    public const int MaxScore = 10;

    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public int AnilistId { get; set; }
    public ListStatus Status { get; set; } = ListStatus.PlanToWatch;
    public int Progress { get; set; }
    public int? Score { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? LastSourceId { get; set; }
    public long Revision { get; set; }
    public long? ManualProgressRevision { get; set; }

    public Anime? Anime { get; set; }

    /// <summary>
    /// Applies watched progress without lowering it. All progress updates use this rule.
    /// </summary>
    public void ApplyProgress(int episode, int? totalEpisodes)
    {
        Progress = Math.Max(Progress, episode);

        if (Status == ListStatus.PlanToWatch)
        {
            Status = ListStatus.Watching;
        }

        StartedAt ??= DateTimeOffset.UtcNow;

        if (totalEpisodes.HasValue && Progress >= totalEpisodes.Value)
        {
            Status = ListStatus.Completed;
            FinishedAt ??= DateTimeOffset.UtcNow;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Manual edit from the UI: set progress to exactly <paramref name="progress"/>,
    /// including lowering it to undo a mistaken "watched". Raising goes through
    /// <see cref="ApplyProgress"/> unchanged. Lowering below the total reopens a
    /// Completed entry as Watching (and forgets FinishedAt); other statuses stay as set.
    /// Imports use ApplyProgress, which never lowers progress.
    /// </summary>
    public void SetProgress(int progress, int? totalEpisodes)
    {
        if (progress >= Progress)
        {
            ApplyProgress(progress, totalEpisodes);
            return;
        }

        Progress = Math.Max(0, progress);

        if (Status == ListStatus.Completed && (totalEpisodes is null || Progress < totalEpisodes.Value))
        {
            Status = ListStatus.Watching;
            FinishedAt = null;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Import merge rule: keep higher progress. Local wins outright when it's
    /// already further along; otherwise the import's progress becomes current and its
    /// score/dates fill in gaps only (never overwrite an existing value). Status is only
    /// adopted from the import when it's a brand-new entry or the import is *strictly*
    /// ahead — on a tie, local status wins too, so re-importing a stale external list
    /// can never regress an already-`Completed` entry back to e.g. `Watching`. Dates/score
    /// must be seeded before <see cref="ApplyProgress"/> runs, so its own StartedAt
    /// fallback only fires when the import had no date either.
    /// </summary>
    public void MergeImport(bool isNewEntry, ListStatus importedStatus, int importedProgress, int? importedScore,
        DateTimeOffset? importedStartedAt, DateTimeOffset? importedFinishedAt, int? totalEpisodes)
    {
        // Import files are user-supplied: same ranges as a manual edit.
        importedProgress = Math.Clamp(importedProgress, 0, MaxEpisode);
        importedScore = importedScore is >= 0 and <= MaxScore ? importedScore : null;
        if (importedProgress < Progress) return;

        var strictlyAhead = isNewEntry || importedProgress > Progress;

        StartedAt ??= importedStartedAt;
        FinishedAt ??= importedFinishedAt;
        Score ??= importedScore;

        ApplyProgress(importedProgress, totalEpisodes);

        if (strictlyAhead)
        {
            Status = importedStatus;
        }
    }
}
