using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.Sync;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Import;

/// <summary>One normalized row to merge, regardless of where it came from (AniList list vs MAL export).</summary>
public record ImportItem(AniListMedia Media, ListStatus Status, int Progress, int? Score,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public static class ImportRunner
{
    public static async Task<ImportSummary> ApplyAsync(AppDbContext db, string userId, IReadOnlyList<ImportItem> items, CancellationToken ct)
    {
        int added = 0, updated = 0, skipped = 0;
        var changedEntries = new List<ListEntry>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await SyncChangeWriter.LockAccountAsync(db, userId, ct);

        foreach (var item in items)
        {
            var anime = await AnimeEndpoints.UpsertAsync(db, item.Media, ct);

            var entry = await db.ListEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anime.AnilistId, ct);
            var isNew = entry is null;
            if (entry is null)
            {
                entry = new ListEntry { Id = Guid.NewGuid(), UserId = userId, AnilistId = anime.AnilistId, Anime = anime };
                db.ListEntries.Add(entry);
            }

            var willApply = isNew || item.Progress >= entry.Progress;
            entry.MergeImport(isNew, item.Status, item.Progress, item.Score, item.StartedAt, item.FinishedAt, anime.Episodes);

            if (isNew) added++;
            else if (willApply) updated++;
            else skipped++;

            if (willApply) changedEntries.Add(entry);
        }

        foreach (var entry in changedEntries)
        {
            await SyncChangeWriter.RecordListEntryAsync(db, entry, manualProgress: false, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ImportSummary(added, updated, skipped, Unmatched: 0);
    }

    public static ListStatus MapAniListStatus(string status) => status switch
    {
        "CURRENT" => ListStatus.Watching,
        "PLANNING" => ListStatus.PlanToWatch,
        "COMPLETED" => ListStatus.Completed,
        "DROPPED" => ListStatus.Dropped,
        "PAUSED" => ListStatus.OnHold,
        "REPEATING" => ListStatus.Rewatching,
        _ => ListStatus.PlanToWatch,
    };
}
