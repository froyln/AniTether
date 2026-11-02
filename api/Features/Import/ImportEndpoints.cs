using System.Security.Claims;
using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using Microsoft.AspNetCore.Identity;

namespace AniTether.Api.Features.Import;

public static class ImportEndpoints
{
    private const long MaxMalFileBytes = 5 * 1024 * 1024;

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var import = app.MapGroup("/api/import").RequireAuthorization().RequireRateLimiting("import");

        import.MapPost("/anilist", async (ImportAniListRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, AppDbContext db, AniListClient aniList, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username required." });
            }

            var remoteEntries = await aniList.GetUserListAsync(request.Username, ct);
            if (remoteEntries is null)
            {
                return Results.NotFound(new { error = "Unknown AniList username." });
            }

            var items = remoteEntries.Select(e => new ImportItem(
                e.Media,
                ImportRunner.MapAniListStatus(e.Status),
                e.Progress,
                e.Score,
                e.StartedAt?.ToDateTimeOffset(),
                e.CompletedAt?.ToDateTimeOffset())).ToList();

            var userId = users.GetUserId(principal)!;
            var summary = await ImportRunner.ApplyAsync(db, userId, items, ct);
            return Results.Ok(summary);
        });

        import.MapPost("/mal", async (HttpRequest http, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, AppDbContext db, AniListClient aniList, CancellationToken ct) =>
        {
            if (!http.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Expected multipart/form-data." });
            }

            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null)
            {
                return Results.BadRequest(new { error = "Missing file." });
            }

            if (file.Length > MaxMalFileBytes)
            {
                return Results.BadRequest(new { error = "File is larger than 5 MB." });
            }

            List<MalListItem> malItems;
            await using (var stream = file.OpenReadStream())
            {
                try
                {
                    malItems = MalXmlParser.Parse(stream).ToList();
                }
                catch (Exception)
                {
                    return Results.BadRequest(new { error = "Could not parse MAL export XML." });
                }
            }

            var mediaByMalId = new Dictionary<int, AniListMedia>();
            foreach (var chunk in malItems.Select(i => i.MalId).Chunk(50))
            {
                foreach (var media in await aniList.GetByMalIdsAsync(chunk, ct))
                {
                    if (media.IdMal is int malId)
                    {
                        mediaByMalId[malId] = media;
                    }
                }
            }

            var items = new List<ImportItem>();
            var unmatched = 0;
            foreach (var malItem in malItems)
            {
                if (!mediaByMalId.TryGetValue(malItem.MalId, out var media))
                {
                    unmatched++;
                    continue;
                }

                items.Add(new ImportItem(media, malItem.Status, malItem.WatchedEpisodes, malItem.Score, malItem.StartedAt, malItem.FinishedAt));
            }

            var userId = users.GetUserId(principal)!;
            var summary = await ImportRunner.ApplyAsync(db, userId, items, ct);
            return Results.Ok(summary with { Unmatched = unmatched });
        });
    }
}
