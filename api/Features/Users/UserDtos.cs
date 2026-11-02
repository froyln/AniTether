namespace AniTether.Api.Features.Users;

/// <summary><c>Role</c> is <c>"Admin"</c>, <c>"Manager"</c> or null (regular user).</summary>
public record CreateUserRequest(string Email, string DisplayName, string Password, string? Role);

/// <summary>Only the fields present are changed. <c>Role</c> is only applied when
/// <c>SetRole</c> is true, since null is itself a valid role (regular user).</summary>
public record UpdateUserRequest(string? DisplayName, bool SetRole, string? Role, bool? Locked);

public record ResetPasswordRequest(string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserResponse(string Id, string Email, string DisplayName, string? Role, bool Locked);
