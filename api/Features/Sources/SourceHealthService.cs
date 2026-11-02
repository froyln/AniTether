using AniTether.Api.Data;
using AniTether.Api.Features.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Sources;

/// <summary>
/// The daily run behind the admin health report. Probes every enabled
/// source, keeps one current <see cref="Data.SourceCheck"/> row per (source, kind), and
/// emails the admins only when a check changed state. Also runs on demand from
/// <c>POST /api/admin/sources/check</c>; one run at a time (<see cref="TryStartInBackground"/>).
/// </summary>
public class SourceHealthService(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpFactory,
    HealthAlertSender alerts,
    IConfiguration config,
    ILogger<SourceHealthService> log) : BackgroundService
{
    /// <summary>Two reference anime: Frieren plus a long-running show.
    /// The second one only exists to answer "is this URL format still right?" when the
    /// first one 404s — many sites simply don't carry a given title, and believing
    /// that first 404 would hide a working source.</summary>
    private const int ReferenceAnilistId = 154587;
    private const int SecondReferenceAnilistId = 196187;

    private int _busy;

    public bool Running => Volatile.Read(ref _busy) == 1;

    /// <summary>Claims the single run slot. False means one is already going.</summary>
    private bool TryReserve() => Interlocked.CompareExchange(ref _busy, 1, 0) == 0;

    /// <summary>Starts a run without waiting for it, or false when one is already going.
    /// The slot is released however the run ends.</summary>
    public bool TryStartInBackground()
    {
        if (!TryReserve())
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunReservedAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "The on-demand source health run failed.");
            }
        });
        return true;
    }

    private async Task RunReservedAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct);
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    /// <summary>True when a run has happened before and the last one is older than the
    /// interval. A database with no runs yet stays idle on purpose: after a deploy the
    /// admin presses "Check now" once and every later run — including the first one
    /// after a restart — is automatic. This is also what keeps the integration tests
    /// from ever making an outbound request on their own.</summary>
    private async Task<bool> NeedsCatchUpAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var newest = await db.SourceChecks.MaxAsync(c => (DateTimeOffset?)c.CheckedAt, ct);
        return newest is not null && newest < DateTimeOffset.UtcNow - Interval;
    }

    private TimeSpan Interval =>
        TimeSpan.FromHours(double.TryParse(config["HealthChecks:IntervalHours"], out var hours) && hours > 0 ? hours : 24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("HealthChecks:Enabled", true))
        {
            log.LogInformation("Source health checks are disabled (HealthChecks__Enabled=false).");
            return;
        }

        // Let the app finish starting before it makes outbound requests.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await NeedsCatchUpAsync(stoppingToken) && TryReserve())
                {
                    await RunReservedAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "The source health run failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Probes one source and returns the outcomes without writing anything —
    /// the "what does this template actually do" check.</summary>
    public async Task<Dictionary<string, CheckOutcome>?> ProbeOneAsync(Guid sourceId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null)
        {
            return null;
        }

        var data = await ProbeDataForAsync(db, source, ReferenceAnilistId, ct);
        if (data is null)
        {
            return null;
        }

        return await ProbeAsync(source, data, await ProbeDataForAsync(db, source, SecondReferenceAnilistId, ct), ct);
    }

    /// <summary>The reference anime plus this source's own slug/link sample for it.
    /// Null when the local cache has no anime to build reference URLs from.</summary>
    public static async Task<ProbeData?> ProbeDataForAsync(AppDbContext db, Data.Source source, int anilistId, CancellationToken ct)
    {
        var anime = await db.Anime.FirstOrDefaultAsync(a => a.AnilistId == anilistId, ct)
            ?? (anilistId == ReferenceAnilistId ? await db.Anime.OrderBy(a => a.AnilistId).FirstOrDefaultAsync(ct) : null);
        if (anime is null)
        {
            return null;
        }

        var link = await db.AnimeSourceLinks
            .FirstOrDefaultAsync(l => l.AnilistId == anime.AnilistId && l.SourceId == source.Id, ct);
        var linkUrls = await db.AnimeSourceLinks
            .Where(l => l.SourceId == source.Id)
            .OrderBy(l => l.Id)
            .Select(l => l.Url)
            .Take(20)
            .ToListAsync(ct);

        return new ProbeData(anime.AnilistId, anime.TitleRomaji, anime.MalId, link?.Slug, linkUrls);
    }

    /// <summary>Unlocked Admins only: a removed (locked) Admin must stop getting source details.</summary>
    public static async Task<IReadOnlyList<string>> AlertRecipientsAsync(UserManager<ApplicationUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin))
            .Where(u => !UserAdmin.IsLocked(u) && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u.Email!)
            .ToList();

    private Task<Dictionary<string, CheckOutcome>> ProbeAsync(Data.Source source, ProbeData data, ProbeData? secondData, CancellationToken ct) =>
        new SourceHealthProbe(httpFactory.CreateClient(nameof(SourceHealthProbe)))
            .ProbeAsync(source, new SourceLinkResolver(httpFactory.CreateClient(nameof(SourceLinkResolver))), data, secondData, ct);

    private async Task RunOnceAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        List<HealthAlertChange> changes;
        IReadOnlyList<string> recipients;

        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var sources = await db.Sources.Where(s => s.Enabled).ToListAsync(ct);
            if (sources.Count == 0)
            {
                log.LogInformation("No enabled sources; nothing to check.");
                return;
            }

            var existing = await db.SourceChecks.ToDictionaryAsync(c => (c.SourceId, c.Kind), ct);
            changes = [];
            foreach (var source in sources)
            {
                if (await ProbeDataForAsync(db, source, ReferenceAnilistId, ct) is not { } data)
                {
                    log.LogWarning("No anime in the local cache to check against; skipping this run.");
                    return;
                }

                var outcomes = await ProbeAsync(source, data, await ProbeDataForAsync(db, source, SecondReferenceAnilistId, ct), ct);

                foreach (var (kind, outcome) in outcomes)
                {
                    if (!existing.TryGetValue((source.Id, kind), out var check))
                    {
                        check = new SourceCheck { SourceId = source.Id, Kind = kind, Result = CheckOutcome.Ok };
                        db.SourceChecks.Add(check);
                        existing[(source.Id, kind)] = check;
                    }

                    var previous = check.Result;
                    SourceHealth.ApplyOutcome(check, outcome, DateTimeOffset.UtcNow);
                    if (SourceHealth.AlertFor(previous, outcome, check.ConsecutiveFailures) is { } change)
                    {
                        changes.Add(new HealthAlertChange(source.Name, kind, change, outcome.Detail, outcome.Hint, SourceHealth.IsHidden(check)));
                    }
                }
            }

            await db.SaveChangesAsync(ct);
            recipients = await AlertRecipientsAsync(users);
        }

        log.LogInformation("Source health run finished in {Seconds:0}s: {Changes} state change(s).", (DateTimeOffset.UtcNow - started).TotalSeconds, changes.Count);
        await alerts.SendAsync(changes, recipients, ct);
    }
}
