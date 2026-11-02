namespace AniTether.Api.Data;

/// <summary>A user's "this didn't work" report from the watch page. The automatic checks
/// can't see a dead player, so this is the only signal that
/// covers the case the user actually notices.</summary>
public class SourceReport
{
    public int Id { get; set; }
    public Guid SourceId { get; set; }
    public required string UserId { get; set; }
    public int? AnilistId { get; set; }
    public int? Episode { get; set; }
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
