using System.Security.Claims;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sources;

public static class SourceHealthEndpoints
{
    public static void MapSourceHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/sources").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        admin.MapGet("/health", async (AppDbContext db, SourceHealthService service, CancellationToken ct) =>
        {
            var sources = await db.Sources.OrderBy(s => s.Name).ToListAsync(ct);
            var checks = await db.SourceChecks.ToListAsync(ct);
            var bySource = checks.GroupBy(c => c.SourceId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Kind).Select(HealthCheckResponse.From).ToList());
            var hidden = checks.Where(SourceHealth.IsHidden).Select(c => c.SourceId).ToHashSet();

            return Results.Ok(new HealthReportResponse(
                service.Running,
                checks.Count == 0 ? null : checks.Max(c => c.CheckedAt),
                sources.Select(s => new HealthSourceResponse(
                    s.Id,
                    s.Name,
                    s.BaseUrl,
                    s.Enabled,
                    !hidden.Contains(s.Id),
                    bySource.TryGetValue(s.Id, out var forSource) ? forSource : new List<HealthCheckResponse>())).ToList(),
                await RecentReportsAsync(db, ct)));
        });

        // Fire-and-forget: a full run is tens of requests at ≥1/s, far too slow to hold an
        // HTTP request open. The report page polls `running` instead.
        admin.MapPost("/check", (SourceHealthService service) =>
            service.TryStartInBackground()
                ? Results.Ok(new { started = true })
                : Results.Conflict(new { error = "A check is already running." }))
            .RequireRateLimiting("healthCheck");

        // One source, no writes: what this row's templates actually do right now.
        admin.MapPost("/check/{sourceId:guid}", async (Guid sourceId, SourceHealthService service, CancellationToken ct) =>
        {
            var outcomes = await service.ProbeOneAsync(sourceId, ct);
            if (outcomes is null)
            {
                return Results.NotFound(new { error = "Unknown source, or no anime in the local cache to check against." });
            }

            var now = DateTimeOffset.UtcNow;
            return Results.Ok(outcomes
                .OrderBy(o => o.Key)
                .Select(o => new HealthCheckResponse(o.Key, o.Value.Result, o.Value.HttpStatus, o.Value.Detail, o.Value.Hint, now, null, 0))
                .ToList());
        }).RequireRateLimiting("healthCheck");

        app.MapPost("/api/sources/{sourceId:guid}/report", async (
            Guid sourceId,
            SourceReportRequest request,
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> users,
            AppDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Sources.AnyAsync(s => s.Id == sourceId, ct))
            {
                return Results.NotFound(new { error = "Unknown source." });
            }

            var url = request.Url?.Trim();
            db.SourceReports.Add(new SourceReport
            {
                SourceId = sourceId,
                UserId = users.GetUserId(principal)!,
                AnilistId = request.AnilistId,
                Episode = request.Episode,
                // Stored only when it is a real absolute https:// URL; the report page
                // links it, so nothing unvalidated is ever rendered as an href.
                Url = url is { Length: > 0 } && SourceUrlValidation.TryParseHttpsUrl(url, out var parsed) ? parsed.ToString() : "",
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        }).RequireAuthorization().RequireRateLimiting("sourceLinks");
    }

    private static async Task<IReadOnlyList<UserReportResponse>> RecentReportsAsync(AppDbContext db, CancellationToken ct)
    {
        var reports = await db.SourceReports.OrderByDescending(r => r.Id).Take(20).ToListAsync(ct);
        var sourceNames = await db.Sources.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var anilistIds = reports.Select(r => r.AnilistId).Where(id => id is not null).Distinct().Select(id => id!.Value).ToList();
        var titles = anilistIds.Count == 0
            ? []
            : await db.Anime.Where(a => anilistIds.Contains(a.AnilistId)).ToDictionaryAsync(a => a.AnilistId, a => a.TitleRomaji, ct);

        return reports
            .Select(r => new UserReportResponse(
                r.Id,
                sourceNames.TryGetValue(r.SourceId, out var name) ? name : "(deleted source)",
                r.AnilistId,
                r.AnilistId is not null && titles.TryGetValue(r.AnilistId.Value, out var title) ? title : null,
                r.Episode,
                r.Url,
                r.CreatedAt))
            .ToList();
    }
}

public record SourceReportRequest(int? AnilistId, int? Episode, string? Url);
