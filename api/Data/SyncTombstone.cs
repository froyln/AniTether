namespace AniTether.Api.Data;

public class SyncTombstone
{
    public required string UserId { get; set; }
    public int AnilistId { get; set; }
    public long Revision { get; set; }
}
