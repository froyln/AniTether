using AniTether.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sources;

public static class AnimeSourceLinkUpserter
{
    public static async Task<(AnimeSourceLink? Link, string? Error)> UpsertAsync(
        AppDbContext db, Guid sourceId, int anilistId, string slug, string url, CancellationToken ct)
    {
        var conflict = await db.AnimeSourceLinks.AnyAsync(
            l => l.SourceId == sourceId && l.Slug == slug && l.AnilistId != anilistId, ct);
        if (conflict)
        {
            return (null, "This URL is already linked to another anime.");
        }

        var link = await db.AnimeSourceLinks.FirstOrDefaultAsync(
            l => l.AnilistId == anilistId && l.SourceId == sourceId, ct);

        if (link is null)
        {
            link = new AnimeSourceLink
            {
                Id = Guid.NewGuid(),
                AnilistId = anilistId,
                SourceId = sourceId,
                Slug = slug,
                Url = url,
            };
            db.AnimeSourceLinks.Add(link);
        }
        else
        {
            link.Slug = slug;
            link.Url = url;
        }

        return (link, null);
    }
}
