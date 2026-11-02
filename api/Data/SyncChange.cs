namespace AniTether.Api.Data;

public class SyncChange
{
    public required string UserId { get; set; }
    public long Cursor { get; set; }
    public required string EntityType { get; set; }
    public required string EntityKey { get; set; }
    public bool IsDeleted { get; set; }
    public string? SnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
