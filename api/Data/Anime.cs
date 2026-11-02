namespace AniTether.Api.Data;

public class Anime
{
    public int AnilistId { get; set; }
    public required string TitleRomaji { get; set; }
    public string? TitleEnglish { get; set; }
    public int? MalId { get; set; }
    public string? CoverUrl { get; set; }
    public int? Episodes { get; set; }
    public string? Format { get; set; }
    public string? AiringStatus { get; set; }
    public string? BannerUrl { get; set; }
    public string? Description { get; set; }
    public string[] Genres { get; set; } = [];
    public int? NextAiringEpisode { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
