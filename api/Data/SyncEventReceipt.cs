namespace AniTether.Api.Data;

public class SyncEventReceipt
{
    public required string UserId { get; set; }
    public Guid EventId { get; set; }
    public int AnilistId { get; set; }
    public required string EntityType { get; set; }
    public int? Episode { get; set; }
    public required string Fingerprint { get; set; }
    public required string Outcome { get; set; }
    public string? Revision { get; set; }
    public long? Cursor { get; set; }
}
