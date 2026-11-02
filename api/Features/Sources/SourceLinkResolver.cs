using System.Text.RegularExpressions;

namespace AniTether.Api.Features.Sources;

/// <summary>
/// Finds an anime's page on a source by GETting the source's own search-results page
/// and matching hrefs against the anime's title — never fetches/proxies video, and only
/// ever hits a source's own https:// host.
/// </summary>
public class SourceLinkResolver(HttpClient http)
{
    private static readonly Regex HrefPattern = new("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Null when the source answered without a match. Throws
    /// <see cref="SourceUnavailableException"/> when it didn't answer.</summary>
    public async Task<Uri?> FindAnimeUrlAsync(Data.Source source, Data.Anime anime, CancellationToken ct)
    {
        if (source.EpisodeUrlTemplate is null)
        {
            return null;
        }

        var expectedSlugs = new[] { anime.TitleRomaji, anime.TitleEnglish }
            .Where(t => t is not null)
            .Select(t => SourceUrlValidation.TitleSlug(t!))
            .Distinct()
            .ToArray();

        foreach (var title in new[] { anime.TitleRomaji, anime.TitleEnglish }.Where(t => t is not null).Distinct())
        {
            var match = await TryOneSearchAsync(source, title!, expectedSlugs, ct);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private async Task<Uri?> TryOneSearchAsync(Data.Source source, string title, string[] expectedSlugs, CancellationToken ct)
    {
        var searchUrl = source.SearchUrlTemplate.Replace("{title}", Uri.EscapeDataString(title));
        if (!SourceUrlValidation.TryParseHttpsUrl(searchUrl, out var uri) || !SourceUrlValidation.HostMatches(uri, source.BaseUrl))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (await SourceHttp.GetBodyAsync(http, request, type => type == "text/html", ct) is not { } html)
        {
            return null;
        }

        foreach (Match hrefMatch in HrefPattern.Matches(html))
        {
            if (!Uri.TryCreate(uri, hrefMatch.Groups[1].Value, out var candidate) ||
                !SourceUrlValidation.HostMatches(candidate, source.BaseUrl))
            {
                continue;
            }

            var slug = SourceUrlValidation.SlugAtTemplatePosition(candidate, source.AnimePageUrlTemplate ?? source.EpisodeUrlTemplate);
            if (slug is not null && expectedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
