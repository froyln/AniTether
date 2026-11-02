namespace AniTether.Api.Data;

public class AnimeSourceLink
{
    public Guid Id { get; set; }
    public int AnilistId { get; set; }
    public Guid SourceId { get; set; }
    public required string Slug { get; set; }
    public required string Url { get; set; }
}
