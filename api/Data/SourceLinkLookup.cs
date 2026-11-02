namespace AniTether.Api.Data;

/// <summary>A recorded miss from the server-side slug lookup, so an anime/source pair
/// that couldn't be resolved isn't retried on every page load. No row means "never
/// checked"; a hit becomes an AnimeSourceLink instead of a row here.</summary>
public class SourceLinkLookup
{
    public int AnilistId { get; set; }
    public Guid SourceId { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
}
