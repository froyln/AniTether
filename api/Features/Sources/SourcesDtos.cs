using AniTether.Api.Data;

namespace AniTether.Api.Features.Sources;

public record CreateSourceRequest(string Name, string BaseUrl, string SearchUrlTemplate, string? EpisodeUrlTemplate = null, string? AnimePageUrlTemplate = null, string? Language = "en", string? EmbedUrlTemplate = null, bool Sandboxed = true, bool AllowPageEmbed = false, bool Enabled = true);

public record UpdateSourceRequest(string? Name, string? BaseUrl, string? SearchUrlTemplate, string? EpisodeUrlTemplate, string? AnimePageUrlTemplate, string? Language, string? EmbedUrlTemplate, bool? Sandboxed, bool? AllowPageEmbed, bool? Enabled);

public record SetSourceLinkRequest(int AnilistId, string Url);

public record SourceResponse(Guid Id, string Name, string BaseUrl, string SearchUrlTemplate, string? EpisodeUrlTemplate, string? AnimePageUrlTemplate, string? Language, string? EmbedUrlTemplate, bool Sandboxed, bool AllowPageEmbed, bool Enabled, bool Healthy)
{
    /// <summary>Healthy = no site/episode/embed check has failed
    /// <see cref="SourceHealth.HideAfterFailures"/> runs in a row. The user-facing
    /// endpoint filters those out entirely, so the frontend never has to remember to
    /// hide them; admin endpoints keep them visible.</summary>
    public static SourceResponse From(Source s, bool healthy = true) => new(s.Id, s.Name, s.BaseUrl, s.SearchUrlTemplate, s.EpisodeUrlTemplate, s.AnimePageUrlTemplate, s.Language, s.EmbedUrlTemplate, s.Sandboxed, s.AllowPageEmbed, s.Enabled, healthy);
}

public record AnimeSourceLinkResponse(Guid SourceId, string Url, string Slug)
{
    public static AnimeSourceLinkResponse From(AnimeSourceLink l) => new(l.SourceId, l.Url, l.Slug);
}
