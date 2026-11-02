using System.Security.Claims;
using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.Sync;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.List;

public static class ListEndpoints
{
    private const int MaxNotesLength = 2000;

    public static void MapListEndpoints(this IEndpointRouteBuilder app)
    {
        var list = app.MapGroup("/api/list").RequireAuthorization();

        list.MapGet("/", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db) =>
        {
            var userId = users.GetUserId(principal)!;
            var entries = await db.ListEntries
                .Include(e => e.Anime)
                .Where(e => e.UserId == userId)
                .ToListAsync();

            var anilistIds = entries.Select(e => e.AnilistId).ToList();
            var links = await db.AnimeSourceLinks.Where(l => anilistIds.Contains(l.AnilistId)).ToListAsync();
            var linksByAnime = links.ToLookup(l => l.AnilistId);

            return Results.Ok(entries.Select(e => ListEntryResponse.From(e, linksByAnime[e.AnilistId])));
        });

        list.MapGet("/{anilistId:int}", async (int anilistId, ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var userId = users.GetUserId(principal)!;
            var entry = await db.ListEntries.Include(e => e.Anime)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anilistId, ct);
            if (entry is null) return Results.NotFound();

            var links = await db.AnimeSourceLinks.Where(l => l.AnilistId == anilistId).ToListAsync(ct);
            return Results.Ok(ListEntryResponse.From(entry, links));
        });

        list.MapPost("/", async (CreateListEntryRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, AniListClient aniList, CancellationToken ct) =>
        {
            var userId = users.GetUserId(principal)!;

            var anime = await db.Anime.FindAsync([request.AnilistId], ct);
            if (anime is null)
            {
                var media = await aniList.GetByIdAsync(request.AnilistId, ct);
                if (media is null)
                {
                    return Results.NotFound(new { error = "Unknown AniList id." });
                }

                anime = await AnimeEndpoints.UpsertAsync(db, media, ct);
            }

            var exists = await db.ListEntries.AnyAsync(e => e.UserId == userId && e.AnilistId == request.AnilistId, ct);
            if (exists)
            {
                return Results.Conflict(new { error = "Already on your list." });
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
            {
                db.ChangeTracker.Clear();
                anime = await db.Anime.FindAsync([request.AnilistId], ct);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncChangeWriter.LockAccountAsync(db, userId, ct);
            if (await db.ListEntries.AnyAsync(e => e.UserId == userId && e.AnilistId == request.AnilistId, ct))
            {
                return Results.Conflict(new { error = "Already on your list." });
            }

            var entry = new ListEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AnilistId = request.AnilistId,
                UpdatedAt = DateTimeOffset.UtcNow,
                Anime = anime,
            };

            db.ListEntries.Add(entry);
            try
            {
                await SyncChangeWriter.RecordListEntryAsync(db, entry, manualProgress: false, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                if (await db.ListEntries.AnyAsync(x => x.UserId == userId && x.AnilistId == request.AnilistId, ct))
                {
                    // A double-click: the other request added it between the check and this save.
                    return Results.Conflict(new { error = "Already on your list." });
                }
                throw;
            }

            return Results.Created($"/api/list/{entry.AnilistId}", ListEntryResponse.From(entry, []));
        }).RequireRateLimiting("aniList"); // may call AniList for an uncached id

        list.MapPatch("/{anilistId:int}", async (int anilistId, UpdateListEntryRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var userId = users.GetUserId(principal)!;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncChangeWriter.LockAccountAsync(db, userId, ct);
            var entry = await db.ListEntries
                .Include(e => e.Anime)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anilistId, ct);

            if (entry is null)
            {
                return Results.NotFound();
            }

            if (request.Progress is int progress)
            {
                if (progress is < 0 or > ListEntry.MaxEpisode)
                {
                    return Results.BadRequest(new { error = $"Progress must be 0-{ListEntry.MaxEpisode}." });
                }
            }

            if (request.Status is ListStatus status)
            {
                entry.Status = status;
            }

            if (request.Score is int score)
            {
                if (score is < 0 or > ListEntry.MaxScore)
                {
                    return Results.BadRequest(new { error = $"Score must be 0-{ListEntry.MaxScore}." });
                }

                entry.Score = score;
            }

            if (request.Notes is not null)
            {
                if (request.Notes.Length > MaxNotesLength)
                {
                    return Results.BadRequest(new { error = $"Notes are limited to {MaxNotesLength} characters." });
                }

                entry.Notes = request.Notes;
            }

            if (request.LastSourceId is Guid sourceId)
            {
                if (!await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
                {
                    return Results.BadRequest(new { error = "Unknown source." });
                }

                entry.LastSourceId = sourceId;
            }

            if (request.Progress is int newProgress)
            {
                if (newProgress > entry.Progress && !AnimeAiring.HasAired(entry.Anime!, newProgress))
                {
                    return Results.BadRequest(new { error = "Episode has not aired yet." });
                }

                entry.SetProgress(newProgress, entry.Anime!.Episodes);
            }

            entry.UpdatedAt = DateTimeOffset.UtcNow;
            await SyncChangeWriter.RecordListEntryAsync(db, entry, request.Progress.HasValue, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var links = await db.AnimeSourceLinks.Where(l => l.AnilistId == anilistId).ToListAsync(ct);
            return Results.Ok(ListEntryResponse.From(entry, links));
        });

        list.MapPost("/{anilistId:int}/apply-progress", async (int anilistId, ApplyProgressRequest request,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            if (request.Episode is < 1 or > ListEntry.MaxEpisode)
            {
                return Results.BadRequest(new { error = $"Episode must be between 1 and {ListEntry.MaxEpisode}." });
            }

            var userId = users.GetUserId(principal)!;
            long? observedRevision = null;
            if (request.ObservedRevision is string revisionText)
            {
                if (!TryRevision(revisionText, out var parsedRevision))
                {
                    return Results.BadRequest(new { error = "Invalid observed revision." });
                }
                observedRevision = parsedRevision;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncChangeWriter.LockAccountAsync(db, userId, ct);
            var entry = await db.ListEntries.Include(e => e.Anime)
                .FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anilistId, ct);
            if (entry is null) return Results.NotFound();
            if (request.Episode > entry.Progress && !AnimeAiring.HasAired(entry.Anime!, request.Episode))
            {
                return Results.BadRequest(new { error = "Episode has not aired yet." });
            }
            if (request.Episode <= entry.Progress)
            {
                var currentLinks = await db.AnimeSourceLinks.Where(l => l.AnilistId == anilistId).ToListAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(ListEntryResponse.From(entry, currentLinks));
            }
            if (observedRevision is long revision && entry.ManualProgressRevision is long manualRevision && manualRevision > revision)
            {
                return Results.Conflict(new { error = "Manual progress changed while this episode was playing." });
            }
            if (request.LastSourceId is Guid sourceId && !await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
            {
                return Results.BadRequest(new { error = "Unknown source." });
            }

            entry.ApplyProgress(request.Episode, entry.Anime!.Episodes);
            if (request.LastSourceId is Guid lastSourceId) entry.LastSourceId = lastSourceId;
            await SyncChangeWriter.RecordListEntryAsync(db, entry, manualProgress: false, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var links = await db.AnimeSourceLinks.Where(l => l.AnilistId == anilistId).ToListAsync(ct);
            return Results.Ok(ListEntryResponse.From(entry, links));
        }).RequireRateLimiting("sync");

        list.MapDelete("/{anilistId:int}", async (int anilistId, ClaimsPrincipal principal, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            var userId = users.GetUserId(principal)!;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await SyncChangeWriter.LockAccountAsync(db, userId, ct);
            var entry = await db.ListEntries.FirstOrDefaultAsync(e => e.UserId == userId && e.AnilistId == anilistId, ct);
            if (entry is null)
            {
                return Results.NotFound();
            }

            await SyncChangeWriter.RecordListDeletionAsync(db, entry, ct);
            db.ListEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
    }

    private static bool TryRevision(string value, out long revision) =>
        long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out revision) && revision >= 0;
}
