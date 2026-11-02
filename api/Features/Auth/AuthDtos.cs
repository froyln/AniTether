namespace AniTether.Api.Features.Auth;

public record LoginRequest(string Email, string Password);
public record MeResponse(string Id, string Email, string DisplayName, bool IsAdmin, bool CanManageUsers, string? PreferredLanguage, string? PreferredAudio, string? Theme, string? ColorMode, bool ProfilePublic, string? ProfileBio, DateTimeOffset? AvatarUpdatedAt);
public record UpdatePreferencesRequest(string? PreferredLanguage, string? PreferredAudio, string? Theme, string? ColorMode);
public record UpdateProfileRequest(string DisplayName, string? Bio, bool ProfilePublic);
