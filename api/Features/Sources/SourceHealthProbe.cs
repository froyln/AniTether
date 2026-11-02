using System.Net.Http.Headers;

namespace AniTether.Api.Features.Sources;

/// <summary>What a probe needs that lives in the database: a reference anime to build
/// reference URLs from, the slug already stored for that anime on this source (if any),
/// and a sample of the source's stored links.</summary>
public sealed record ProbeData(int AnilistId, string TitleRomaji, int? MalId, string? Slug, IReadOnlyList<string> LinkUrls);

/// <summary>
/// Runs the health checks for one source. Same guarded HttpClient settings as
/// <see cref="SourceLinkResolver"/> (5 s timeout via Program.cs, no auto-redirect, small
/// body cap) and only ever hits hosts already in <c>sources</c> — admin-controlled, never
/// user-supplied. Never fetches a video or a stream URL: an embed check reads status and
/// headers only (plus a few KB for the block-page sniff, see <see cref="SourceHealth"/>).
/// </summary>
public class SourceHealthProbe(HttpClient http)
{
    private const int MaxPrefixBytes = 8_000;

    // ≤ 1 request/second per host. Every request in one
    // probe goes to the same source host, so a delay between them covers the rule.
    private static readonly TimeSpan HostDelay = TimeSpan.FromSeconds(1);

    public async Task<Dictionary<string, CheckOutcome>> ProbeAsync(Data.Source source, SourceLinkResolver resolver, ProbeData data, ProbeData? secondData, CancellationToken ct)
    {
        var results = new Dictionary<string, CheckOutcome>();

        results[Data.SourceCheckKinds.Site] = await CheckSiteAsync(source, ct);
        await Task.Delay(HostDelay, ct);

        results[Data.SourceCheckKinds.Search] = await CheckSearchAsync(resolver, source, data, ct);
        await Task.Delay(HostDelay, ct);

        results[Data.SourceCheckKinds.Episode] = await CheckEpisodeAsync(source, data, secondData, ct);
        await Task.Delay(HostDelay, ct);

        results[Data.SourceCheckKinds.Embed] = await CheckEmbedAsync(source, data, ct);
        await Task.Delay(HostDelay, ct);

        results[Data.SourceCheckKinds.Links] = await CheckLinksAsync(source, data, ct);

        return results;
    }

    private async Task<CheckOutcome> CheckSiteAsync(Data.Source source, CancellationToken ct)
    {
        if (!SourceUrlValidation.TryParseHttpsUrl(source.BaseUrl, out var url))
        {
            return new(CheckOutcome.Broken, null, "Base URL is not an absolute https:// URL.", "Fix the base URL in the source settings.");
        }

        var (status, headers, prefix) = await GetPrefixAsync(url, ct);
        return SourceHealth.Site(status, SourceHealth.LooksBlocked(prefix), MovedHost(url, headers));
    }

    private async Task<CheckOutcome> CheckSearchAsync(SourceLinkResolver resolver, Data.Source source, ProbeData data, CancellationToken ct)
    {
        Uri? match;
        try
        {
            match = await resolver.FindAnimeUrlAsync(source, ReferenceAnime(data), ct);
        }
        catch (SourceUnavailableException)
        {
            return new(CheckOutcome.Unknown, null, "Its search page didn't answer.", "Usually temporary; the next run checks again.");
        }

        return match is not null
            ? new(CheckOutcome.Ok, null, $"Its own search finds \"{data.TitleRomaji}\" ({match}).", null)
            : new(CheckOutcome.Unknown, null, "No matching link in its own search results.", "May render with JavaScript, or the site's titles may have changed. Check by hand in a browser.");
    }

    private async Task<CheckOutcome> CheckEpisodeAsync(Data.Source source, ProbeData data, ProbeData? secondData, CancellationToken ct)
    {
        if (source.EpisodeUrlTemplate is null)
        {
            return new(CheckOutcome.Unknown, null, "No episode URL template — this source only links to an anime page.", null);
        }

        if (!SourceUrlValidation.TryBuildUrl(source.EpisodeUrlTemplate, data.AnilistId, data.MalId, data.Slug, 1, out var url, out var reason))
        {
            return new(CheckOutcome.Unknown, null, reason, "Save a link for this anime on this source, then re-run the check.");
        }

        var (status, headers, prefix) = await GetPrefixAsync(url!, ct);
        var outcome = SourceHealth.Page(status, SourceHealth.LooksBlocked(prefix), MovedHost(url!, headers), "Reference episode URL works.", "Paste a working episode URL on the anime page; the server derives the new template from it.");

        if (outcome.Result != CheckOutcome.Broken || outcome.HttpStatus != 404)
        {
            return outcome;
        }

        // A 404 for one anime usually means the site doesn't carry that title, not that the
        // URL format changed: a site may 404 for one show while the same /watch/{anilist_id}
        // URL still works for another. Believing the first 404 would mark a working template
        // broken and hide the source after three runs.
        // When a second reference anime proves the format still works, the answer is unknown.
        if (secondData is null || !SourceUrlValidation.TryBuildUrl(source.EpisodeUrlTemplate, secondData.AnilistId, secondData.MalId, secondData.Slug, 1, out var secondUrl, out _))
        {
            return outcome;
        }

        await Task.Delay(HostDelay, ct);
        var (secondStatus, _, secondPrefix) = await GetPrefixAsync(secondUrl!, ct);
        return secondStatus is >= 200 and < 400 && !SourceHealth.LooksBlocked(secondPrefix)
            ? new(CheckOutcome.Unknown, outcome.HttpStatus, $"404 for \"{data.TitleRomaji}\", but the same URL format works for \"{secondData.TitleRomaji}\" on this site.", "Probably just doesn't carry that title. Nothing to fix.")
            : outcome;
    }

    private async Task<CheckOutcome> CheckEmbedAsync(Data.Source source, ProbeData data, CancellationToken ct)
    {
        if (source.EmbedUrlTemplate is null)
        {
            return new(CheckOutcome.Unknown, null, "No embed URL template — this source opens in a new tab.", null);
        }

        if (!SourceUrlValidation.TryBuildUrl(source.EmbedUrlTemplate, data.AnilistId, data.MalId, data.Slug, 1, out var url, out var reason))
        {
            return new(CheckOutcome.Unknown, null, reason, "This embed needs a per-anime link that isn't saved yet.");
        }

        var (status, headers, prefix) = await GetPrefixAsync(url!, ct);
        return SourceHealth.Embed(
            status,
            SourceHealth.LooksBlocked(prefix),
            Header(headers, "X-Frame-Options"),
            FrameAncestors(headers));
    }

    private async Task<CheckOutcome> CheckLinksAsync(Data.Source source, ProbeData data, CancellationToken ct)
    {
        if (data.LinkUrls.Count == 0)
        {
            return new(CheckOutcome.Ok, null, "No stored links for this source yet.", null);
        }

        int stale = 0;
        string? firstStale = null;
        foreach (var link in data.LinkUrls)
        {
            ct.ThrowIfCancellationRequested();
            if (!SourceUrlValidation.TryParseHttpsUrl(link, out var url) || !SourceUrlValidation.HostMatches(url, source.BaseUrl))
            {
                continue;
            }

            var (status, _, _) = await GetPrefixAsync(url, ct);
            if (status == 404)
            {
                stale++;
                firstStale ??= url.ToString();
            }

            await Task.Delay(HostDelay, ct);
        }

        return stale == 0
            ? new(CheckOutcome.Ok, null, $"All {data.LinkUrls.Count} sampled links resolve.", null)
            : new(CheckOutcome.Broken, null, $"{stale} of {data.LinkUrls.Count} sampled links are 404, e.g. {firstStale}.", "Re-save or re-resolve those links from the anime page. Stale links are never auto-deleted.");
    }

    private static Data.Anime ReferenceAnime(ProbeData data) => new()
    {
        AnilistId = data.AnilistId,
        TitleRomaji = data.TitleRomaji,
        MalId = data.MalId,
        SyncedAt = DateTimeOffset.UnixEpoch,
    };

    private async Task<(int? Status, HttpResponseHeaders Headers, string Prefix)> GetPrefixAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await SourceHttp.GetAsync(http, request, MaxPrefixBytes, ct);
            return (response.Status, response.Headers, response.Body);
        }
        catch (SourceUnavailableException)
        {
            return (null, new HttpResponseMessage().Headers, "");
        }
    }

    private static string? MovedHost(Uri requested, HttpResponseHeaders headers) =>
        // AllowAutoRedirect is off, so a redirect is observed rather than followed.
        headers.Location is { } location && Uri.TryCreate(requested, location, out var target) &&
        !string.Equals(target.Host, requested.Host, StringComparison.OrdinalIgnoreCase)
            ? target.Host
            : null;

    private static string? Header(HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

    private static string? FrameAncestors(HttpResponseHeaders headers)
    {
        if (Header(headers, "Content-Security-Policy") is not { Length: > 0 } csp)
        {
            return null;
        }

        var directive = csp.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(d => d.StartsWith("frame-ancestors", StringComparison.OrdinalIgnoreCase));
        var value = directive?["frame-ancestors".Length..].Trim().Trim('\'');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
