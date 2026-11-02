using System.Security.Claims;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sources;

public static class SourcesEndpoints
{
    public static void MapSourcesEndpoints(this IEndpointRouteBuilder app)
    {
        var sources = app.MapGroup("/api/sources").RequireAuthorization();

        sources.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            // Enabled AND healthy: after SourceHealth.HideAfterFailures consecutive broken
            // runs a source disappears from the user-facing list, which is what hides its
            // watch buttons everywhere at once (they all read from this endpoint).
            var enabled = await db.Sources.Where(s => s.Enabled).ToListAsync(ct);
            var hidden = await HiddenSourceIdsAsync(db, enabled.Select(s => s.Id).ToList(), ct);
            return Results.Ok(enabled.Where(s => !hidden.Contains(s.Id)).Select(s => SourceResponse.From(s)));
        });

        sources.MapGet("/all", async (AppDbContext db, CancellationToken ct) =>
        {
            var all = await db.Sources.ToListAsync(ct);
            var hidden = await HiddenSourceIdsAsync(db, all.Select(s => s.Id).ToList(), ct);
            return Results.Ok(all.Select(s => SourceResponse.From(s, !hidden.Contains(s.Id))));
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin));

        sources.MapPost("/", async (CreateSourceRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Name is required." });
            }

            var source = new Source
            {
                Id = Guid.NewGuid(),
                Name = request.Name?.Trim() ?? "",
                BaseUrl = request.BaseUrl,
                SearchUrlTemplate = request.SearchUrlTemplate,
                EpisodeUrlTemplate = string.IsNullOrWhiteSpace(request.EpisodeUrlTemplate) ? null : request.EpisodeUrlTemplate,
                AnimePageUrlTemplate = string.IsNullOrWhiteSpace(request.AnimePageUrlTemplate) ? null : request.AnimePageUrlTemplate,
                Language = request.Language,
                EmbedUrlTemplate = string.IsNullOrWhiteSpace(request.EmbedUrlTemplate) ? null : request.EmbedUrlTemplate,
                Sandboxed = request.Sandboxed,
                AllowPageEmbed = request.AllowPageEmbed,
                Enabled = request.Enabled,
            };

            if (!Languages.Allowed.Contains(source.Language))
            {
                return Results.BadRequest(new { error = $"Language must be one of: {string.Join(", ", Languages.Allowed)}." });
            }

            if (ValidateTemplates(source) is { } error)
            {
                return Results.BadRequest(new { error });
            }

            db.Sources.Add(source);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sources/{source.Id}", SourceResponse.From(source));
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin));

        sources.MapPatch("/{id:guid}", async (Guid id, UpdateSourceRequest request, AppDbContext db, CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([id], ct);
            if (source is null)
            {
                return Results.NotFound();
            }

            if (request.SearchUrlTemplate is not null)
            {
                source.SearchUrlTemplate = request.SearchUrlTemplate;
            }

            if (request.EpisodeUrlTemplate is not null)
            {
                source.EpisodeUrlTemplate = request.EpisodeUrlTemplate.Length == 0 ? null : request.EpisodeUrlTemplate;
            }

            if (request.AnimePageUrlTemplate is not null)
            {
                source.AnimePageUrlTemplate = request.AnimePageUrlTemplate.Length == 0 ? null : request.AnimePageUrlTemplate;
            }

            if (request.Language is not null)
            {
                if (!Languages.Allowed.Contains(request.Language))
                {
                    return Results.BadRequest(new { error = $"Language must be one of: {string.Join(", ", Languages.Allowed)}." });
                }

                source.Language = request.Language;
            }

            if (request.EmbedUrlTemplate is not null)
            {
                source.EmbedUrlTemplate = request.EmbedUrlTemplate.Length == 0 ? null : request.EmbedUrlTemplate;
            }

            if (request.Sandboxed is bool sandboxed)
            {
                source.Sandboxed = sandboxed;
            }

            if (request.AllowPageEmbed is bool allowPageEmbed)
            {
                source.AllowPageEmbed = allowPageEmbed;
            }

            if (request.Name is not null)
            {
                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    return Results.BadRequest(new { error = "Name is required." });
                }

                source.Name = request.Name.Trim();
            }

            if (request.BaseUrl is not null)
            {
                source.BaseUrl = request.BaseUrl;
            }

            if (request.Enabled is bool enabled)
            {
                source.Enabled = enabled;
            }

            if (ValidateTemplates(source, request) is { } error)
            {
                return Results.BadRequest(new { error });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(SourceResponse.From(source));
        }).RequireAuthorization(p => p.RequireRole(Roles.Admin));

        sources.MapPost("/{sourceId:guid}/links", async (
            Guid sourceId,
            SetSourceLinkRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var source = await db.Sources.FindAsync([sourceId], ct);
            if (source is null)
            {
                return Results.NotFound(new { error = "Unknown source." });
            }

            var anime = await db.Anime.FindAsync([request.AnilistId], ct);
            if (anime is null)
            {
                return Results.NotFound(new { error = "Unknown anime." });
            }

            if (!SourceUrlValidation.TryParseHttpsUrl(request.Url, out var url) ||
                !SourceUrlValidation.HostMatches(url, source.BaseUrl))
            {
                return Results.BadRequest(new { error = "URL must be an absolute https:// URL on the source's host." });
            }

            var slug = SourceUrlValidation.DeriveSlug(url, source.AnimePageUrlTemplate ?? source.EpisodeUrlTemplate);
            if (slug is null)
            {
                return Results.BadRequest(new { error = "URL must include a path identifying the anime." });
            }

            var (link, error) = await AnimeSourceLinkUpserter.UpsertAsync(db, sourceId, request.AnilistId, slug, request.Url, ct);
            if (error is not null)
            {
                return Results.Conflict(new { error });
            }

            var userId = users.GetUserId(principal)!;
            var entry = await db.ListEntries.FirstOrDefaultAsync(
                e => e.UserId == userId && e.AnilistId == request.AnilistId, ct);
            if (entry is not null)
            {
                entry.LastSourceId = sourceId;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (AppDbContext.IsUniqueViolation(e))
            {
                return Results.Conflict(new { error = "Someone just linked this anime on that source. Reload and check." });
            }
            return Results.Ok(AnimeSourceLinkResponse.From(link!));
        }).RequireRateLimiting("sourceLinks");
    }

    private static string? ValidateTemplates(Source source, UpdateSourceRequest? patch = null)
    {
        var all = patch is null || patch.BaseUrl is not null;
        if (all && !SourceUrlValidation.TryParseHttpsUrl(source.BaseUrl, out _))
            return "Base URL must be an absolute https:// URL.";
        if ((all || patch!.SearchUrlTemplate is not null) &&
            !SourceUrlValidation.IsValidTemplate(source.SearchUrlTemplate, source.BaseUrl, "{title}"))
            return "Search URL template must be an absolute https:// URL on the base URL's host and contain {title}.";
        if ((all || patch!.EpisodeUrlTemplate is not null) && source.EpisodeUrlTemplate is not null &&
            !SourceUrlValidation.IsValidTemplate(source.EpisodeUrlTemplate, source.BaseUrl, "{episode}"))
            return "Episode URL template must be an absolute https:// URL on the base URL's host and contain {episode}.";
        if ((all || patch!.AnimePageUrlTemplate is not null) && source.AnimePageUrlTemplate is not null &&
            !SourceUrlValidation.IsValidTemplate(source.AnimePageUrlTemplate, source.BaseUrl, "{slug}"))
            return "Anime page URL template must be an absolute https:// URL on the base URL's host and contain {slug}.";
        if ((all || patch!.EmbedUrlTemplate is not null) && source.EmbedUrlTemplate is not null &&
            !SourceUrlValidation.IsValidTemplate(source.EmbedUrlTemplate, source.BaseUrl, "{episode}"))
            return "Embed URL template must be an absolute https:// URL on the base URL's host and contain {episode}.";
        return null;
    }

    private static async Task<HashSet<Guid>> HiddenSourceIdsAsync(AppDbContext db, List<Guid> sourceIds, CancellationToken ct)
    {
        if (sourceIds.Count == 0)
        {
            return [];
        }

        var hidden = await db.SourceChecks
            .Where(c => sourceIds.Contains(c.SourceId) && c.Result == CheckOutcome.Broken && c.ConsecutiveFailures >= SourceHealth.HideAfterFailures)
            .Select(c => c.SourceId)
            .ToListAsync(ct);
        return [.. hidden.Distinct()];
    }
}
