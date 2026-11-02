namespace AniTether.Api.Data;

public class SyncAccountState
{
    public required string UserId { get; set; }
    public long Cursor { get; set; }
    public long HistoryFloor { get; set; }
}
