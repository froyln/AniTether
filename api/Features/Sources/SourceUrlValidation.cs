using System.Text.RegularExpressions;

namespace AniTether.Api.Features.Sources;

public static class SourceUrlValidation
{
    private static readonly Regex PlaceholderPattern = new(@"\{[a-z_]+\}", RegexOptions.Compiled);

    /// <summary>The site-agnostic matching key for a title: lowercase, non-alphanumeric
    /// runs collapsed to a single '-', trimmed. Matches how many sites derive their own
    /// slugs from the AniList romaji title. Used only to recognize a match during
    /// server-side slug lookup — never filled into a URL template directly (a guessed
    /// slug is worse than no button when wrong).</summary>
    public static string TitleSlug(string title) =>
        Regex.Replace(title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public static bool TryParseHttpsUrl(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            parsed.Port != 443 || parsed.UserInfo.Length != 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool HostMatches(Uri url, string baseUrl) =>
        TryParseHttpsUrl(baseUrl, out var baseUri) &&
        string.Equals(url.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>A URL template is valid when it's an absolute
    /// https:// URL on the source's own host and still contains its placeholder.</summary>
    public static bool IsValidTemplate(string template, string baseUrl, string placeholder) =>
        TryParseHttpsUrl(template, out var uri) &&
        HostMatches(uri, baseUrl) &&
        template.Contains(placeholder, StringComparison.Ordinal);

    /// <summary>Fill a source URL only when every required anime identifier is present.
    /// A missing slug or MAL id must not turn into a URL that falsely looks valid.</summary>
    public static bool TryBuildUrl(string template, int anilistId, int? malId, string? slug,
        int episode, out Uri? url, out string reason)
    {
        url = null;
        if (template.Contains("{slug}", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(slug))
        {
            reason = "The URL needs a per-anime link for this source, and none is saved yet.";
            return false;
        }

        if (template.Contains("{mal_id}", StringComparison.Ordinal) && malId is null)
        {
            reason = "The URL needs the anime's MyAnimeList id, which not every anime has.";
            return false;
        }

        var filled = template
            .Replace("{anilist_id}", anilistId.ToString(), StringComparison.Ordinal)
            .Replace("{mal_id}", malId?.ToString() ?? "", StringComparison.Ordinal)
            .Replace("{episode}", episode.ToString(), StringComparison.Ordinal)
            .Replace("{slug}", Uri.EscapeDataString(slug ?? ""), StringComparison.Ordinal)
            .Replace("{audio}", "sub", StringComparison.Ordinal);

        if (PlaceholderPattern.IsMatch(filled))
        {
            reason = "The URL template has a placeholder this check doesn't know how to fill.";
            return false;
        }

        reason = "";
        return TryParseHttpsUrl(filled, out url);
    }

    /// <summary>
    /// The anime's slug on a source, from a pasted URL. When the source's episode
    /// template has a <c>{slug}</c> segment (e.g. <c>/anime/{slug}/episode/{episode}</c>, or one
    /// wrapped in literal text like <c>/ver/{slug}-episodio-{episode}</c>)
    /// and the URL's leading segments match the template's, the slug is the segment in
    /// that position — so pasting an episode page works as well as the anime page
    /// (some sites redirect the anime page straight to episode 1). Otherwise: the last
    /// path segment.
    /// </summary>
    public static string? DeriveSlug(Uri url, string? episodeUrlTemplate = null)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return SlugAtTemplatePosition(url, episodeUrlTemplate) ?? Uri.UnescapeDataString(segments[^1]);
    }

    /// <summary>The slug at the episode template's <c>{slug}</c> position, only when the
    /// URL's leading segments match the template's leading segments exactly. Unlike
    /// <see cref="DeriveSlug"/>, this never falls back to the last path segment — used
    /// when scanning arbitrary hrefs from a search-results page, where an unrelated link
    /// (nav, footer) must not be mistaken for a match.</summary>
    public static string? SlugAtTemplatePosition(Uri url, string? episodeUrlTemplate)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var templateSegments = TemplatePathSegments(episodeUrlTemplate);
        var slugIndex = Array.FindIndex(templateSegments, s => s.Contains("{slug}", StringComparison.Ordinal));
        if (slugIndex < 0 || segments.Length <= slugIndex ||
            !templateSegments.Take(slugIndex).SequenceEqual(segments.Take(slugIndex), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return SlugInSegment(Uri.UnescapeDataString(segments[slugIndex]), templateSegments[slugIndex]);
    }

    /// <summary>
    /// The slug inside one path segment. Usually the template segment is exactly
    /// <c>{slug}</c>, but some sites wrap it in literal text — e.g. an anime page of
    /// <c>{slug}-sub-espanol</c> and an episode page of <c>{slug}-episodio-{episode}</c>.
    /// The literal before <c>{slug}</c> must be the segment's prefix; the literal after it
    /// (up to the next placeholder) must be its suffix, or — when another placeholder follows
    /// — its last occurrence ends the slug. A literal-free <c>{slug}{episode}</c> is
    /// ambiguous and never matches.
    /// </summary>
    private static string? SlugInSegment(string segment, string templateSegment)
    {
        var at = templateSegment.IndexOf("{slug}", StringComparison.Ordinal);
        var prefix = templateSegment[..at];
        var rest = templateSegment[(at + "{slug}".Length)..];
        var nextPlaceholder = rest.IndexOf('{');
        var suffix = nextPlaceholder < 0 ? rest : rest[..nextPlaceholder];
        if (!segment.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int end;
        if (nextPlaceholder < 0)
        {
            end = segment.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? segment.Length - suffix.Length : -1;
        }
        else
        {
            end = suffix.Length == 0 ? -1 : segment.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return end > prefix.Length ? segment[prefix.Length..end] : null;
    }

    // Split by hand: Uri would percent-encode the {placeholder} braces.
    private static string[] TemplatePathSegments(string? template)
    {
        if (template is null)
        {
            return [];
        }

        var afterScheme = template.IndexOf("://", StringComparison.Ordinal);
        var pathStart = afterScheme < 0 ? -1 : template.IndexOf('/', afterScheme + 3);
        if (pathStart < 0)
        {
            return [];
        }

        var path = template[pathStart..].Split('?', '#')[0];
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
