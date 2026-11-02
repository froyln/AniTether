using AniTether.Api.Data;
using AniTether.Api.Features.Sources;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Anime;

public static class AnimeEndpoints
{
    // AGENTS.md → Conventions: re-sync when synced_at is older than 7 days, or the
    // show is still airing (episode count/nextAiringEpisode can change any day).
    private static readonly TimeSpan ReSyncAfter = TimeSpan.FromDays(7);

    private const int MaxQueryLength = 100;

    public static void MapAnimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/anime/search", async (string q, AniListClient aniList, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.Ok(Array.Empty<Data.Anime>());
            }

            if (q.Length > MaxQueryLength)
            {
                return Results.BadRequest(new { error = $"Search is limited to {MaxQueryLength} characters." });
            }

            var results = await aniList.SearchAsync(q, ct);
            var cached = new List<Data.Anime>();
            foreach (var media in results)
            {
                cached.Add(await UpsertAsync(db, media, ct));
            }

            await SaveCacheAsync(db, ct);
            return Results.Ok(cached);
        }).RequireAuthorization().RequireRateLimiting("aniList");

        app.MapGet("/api/catalog", async (int? page, string? q, string? genres, string? format, int? year, string? season,
            string? status, string? sort, AniListClient aniList, AppDbContext db, CancellationToken ct) =>
        {
            var (filter, error) = CatalogFilter.Parse(q, genres, format, year, season, status, sort);
            if (filter is null)
            {
                return Results.BadRequest(new { error });
            }

            var results = await aniList.BrowseAsync(page is > 1 ? page.Value : 1, filter, ct);
            var cached = new List<Data.Anime>();
            foreach (var media in results)
            {
                cached.Add(await UpsertAsync(db, media, ct));
            }

            await SaveCacheAsync(db, ct);
            return Results.Ok(cached);
        }).RequireAuthorization().RequireRateLimiting("aniList");

        // Categories page tile images, for the server's own fixed genre list (CatalogFilter.AllGenres).
        app.MapGet("/api/catalog/genre-images", (AniListClient aniList, CancellationToken ct) => aniList.GetGenreImagesAsync(ct))
            .RequireAuthorization().RequireRateLimiting("aniList");

        app.MapGet("/api/anime/{id:int}", async (int id, AniListClient aniList, AppDbContext db, CancellationToken ct) =>
        {
            var anime = await db.Anime.FindAsync([id], ct);
            var stale = anime is null || DateTimeOffset.UtcNow - anime.SyncedAt > ReSyncAfter || anime.AiringStatus is "RELEASING" or "NOT_YET_RELEASED";

            if (stale)
            {
                AniListMedia? media;
                try
                {
                    media = await aniList.GetByIdAsync(id, ct);
                }
                catch (Exception e) when (anime is not null && (e is HttpRequestException or System.Text.Json.JsonException || (e is TaskCanceledException && !ct.IsCancellationRequested)))
                {
                    // AniList down or rate-limited: the cached row beats an error page.
                    media = null;
                }

                if (media is not null)
                {
                    anime = await UpsertAsync(db, media, ct);
                    await SaveCacheAsync(db, ct);
                }
            }

            if (anime is null)
            {
                return Results.NotFound(new { error = "Unknown AniList id." });
            }

            var links = await db.AnimeSourceLinks.Where(l => l.AnilistId == id).ToListAsync(ct);
            return Results.Ok(new AnimeDetailResponse(anime, links.Select(AnimeSourceLinkResponse.From).ToList()));
        }).RequireAuthorization().RequireRateLimiting("aniList");

        app.MapPost("/api/anime/{id:int}/resolve-links", async (int id, AppDbContext db, SourceLinkResolver resolver, CancellationToken ct) =>
        {
            var anime = await db.Anime.FindAsync([id], ct);
            if (anime is null)
            {
                return Results.NotFound(new { error = "Unknown AniList id." });
            }

            var linkedSourceIds = await db.AnimeSourceLinks.Where(l => l.AnilistId == id).Select(l => l.SourceId).ToListAsync(ct);
            var recentlyMissedSourceIds = await db.SourceLinkLookups
                .Where(l => l.AnilistId == id && l.CheckedAt > DateTimeOffset.UtcNow - ReSyncAfter)
                .Select(l => l.SourceId)
                .ToListAsync(ct);

            var candidates = await db.Sources
                .Where(s => s.Enabled && s.EpisodeUrlTemplate != null && s.EpisodeUrlTemplate.Contains("{slug}"))
                .Where(s => !linkedSourceIds.Contains(s.Id) && !recentlyMissedSourceIds.Contains(s.Id))
                .ToListAsync(ct);

            var results = await Task.WhenAll(candidates.Select(async source =>
            {
                try
                {
                    return (source, url: await resolver.FindAnimeUrlAsync(source, anime, ct), answered: true);
                }
                catch (SourceUnavailableException)
                {
                    return (source, url: (Uri?)null, answered: false);
                }
            }));

            // A source that didn't answer gets no miss row, so the next page view retries it.
            foreach (var (source, url, _) in results.Where(r => r.answered))
            {
                if (url is not null && SourceUrlValidation.TryParseHttpsUrl(url.ToString(), out _))
                {
                    var slug = SourceUrlValidation.DeriveSlug(url, source.AnimePageUrlTemplate ?? source.EpisodeUrlTemplate);
                    if (slug is not null)
                    {
                        await AnimeSourceLinkUpserter.UpsertAsync(db, source.Id, id, slug, url.ToString(), ct);
                        continue;
                    }
                }

                var lookup = await db.SourceLinkLookups.FindAsync([id, source.Id], ct);
                if (lookup is null)
                {
                    db.SourceLinkLookups.Add(new SourceLinkLookup { AnilistId = id, SourceId = source.Id, CheckedAt = DateTimeOffset.UtcNow });
                }
                else
                {
                    lookup.CheckedAt = DateTimeOffset.UtcNow;
                }
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
            {
                // A concurrent resolve for this anime stored the same link or miss first.
                db.ChangeTracker.Clear();
            }

            var links = await db.AnimeSourceLinks.Where(l => l.AnilistId == id).ToListAsync(ct);
            return Results.Ok(new AnimeDetailResponse(anime, links.Select(AnimeSourceLinkResponse.From).ToList()));
        }).RequireAuthorization().RequireRateLimiting("resolveLinks");

    }

    /// <summary>Saves AniList cache rows. Two requests caching the same anime at once race on
    /// its key; the loser's copy is the same data, so it's dropped instead of failing the request.</summary>
    private static async Task SaveCacheAsync(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
        {
            db.ChangeTracker.Clear();
        }
    }

    public static async Task<Data.Anime> UpsertAsync(AppDbContext db, AniListMedia media, CancellationToken ct)
    {
        var existing = await db.Anime.FindAsync([media.Id], ct);
        if (existing is null)
        {
            existing = new Data.Anime { AnilistId = media.Id, TitleRomaji = media.Title.Romaji };
            db.Anime.Add(existing);
        }

        existing.TitleRomaji = media.Title.Romaji;
        existing.TitleEnglish = media.Title.English;
        existing.MalId = media.IdMal;
        existing.CoverUrl = media.CoverImage?.Large;
        existing.BannerUrl = media.BannerImage;
        existing.Description = media.Description;
        existing.Genres = media.Genres?.ToArray() ?? [];
        existing.Episodes = media.Episodes;
        existing.Format = media.Format;
        existing.AiringStatus = media.Status;
        existing.NextAiringEpisode = media.NextAiringEpisode?.Episode;
        existing.SyncedAt = DateTimeOffset.UtcNow;

        return existing;
    }
}
