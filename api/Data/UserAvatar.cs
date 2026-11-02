namespace AniTether.Api.Data;

public class UserAvatar
{
    public required string UserId { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
