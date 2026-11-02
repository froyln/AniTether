namespace AniTether.Api.Data;

public class ResumePosition
{
    public required string UserId { get; set; }
    public int AnilistId { get; set; }
    public int Episode { get; set; }
    public Guid SessionId { get; set; }
    public long Revision { get; set; }
    public long Sequence { get; set; }
    public long PositionMs { get; set; }
    public long? DurationMs { get; set; }
}
