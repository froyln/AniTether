namespace AniTether.Api.Data;

public class Source
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string BaseUrl { get; set; }
    public required string SearchUrlTemplate { get; set; }
    public string? EpisodeUrlTemplate { get; set; }

    /// <summary>The site's own anime info page (e.g. https://example.com/anime/{slug}),
    /// only used by <see cref="Features.Sources.SourceLinkResolver"/> to match search-page
    /// hrefs when that page's URL shape differs from <see cref="EpisodeUrlTemplate"/>'s
    /// (episode lives under a different path prefix). When null, the resolver matches
    /// against EpisodeUrlTemplate.</summary>
    public string? AnimePageUrlTemplate { get; set; }

    /// <summary>Whitelist in <see cref="Languages"/>. Nullable in the schema;
    /// new sources require a language.</summary>
    public string? Language { get; set; }

    /// <summary>An embed-only player URL (e.g.
    /// https://example.com/embed/ani/{anilist_id}/{episode}), meant to be framed in an
    /// iframe rather than opened in a new tab. Validated the same way as the other
    /// templates (https, own host, requires {episode}).</summary>
    public string? EmbedUrlTemplate { get; set; }

    /// <summary>Whether the watch page's iframe applies a `sandbox` attribute for this
    /// source. Defaults true; some embed players require it turned off.</summary>
    public bool Sandboxed { get; set; } = true;

    /// <summary>When true, the watch page may also frame the source's own
    /// episode page (its <see cref="EpisodeUrlTemplate"/> URL) in-place instead of only
    /// the explicit <see cref="EmbedUrlTemplate"/>. Several real sites frame cleanly
    /// (no X-Frame-Options) but bring their own nav/ads along; enabled per source only
    /// after a live playback check. Defaults false.</summary>
    public bool AllowPageEmbed { get; set; }

    public bool Enabled { get; set; } = true;
}
