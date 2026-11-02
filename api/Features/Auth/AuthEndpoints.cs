using System.Security.Claims;
using AniTether.Api.Data;
using AniTether.Api.Features.Users;
using Microsoft.AspNetCore.Identity;

namespace AniTether.Api.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var user = await users.FindByEmailAsync(request.Email);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: true, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await ToResponseAsync(users, user));
        });

        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();

        app.MapGet("/api/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await ToResponseAsync(users, user));
        }).RequireAuthorization();

        app.MapPatch("/api/me/preferences", async (UpdatePreferencesRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            if (request.PreferredLanguage is not null && !Languages.Allowed.Contains(request.PreferredLanguage))
            {
                return Results.BadRequest(new { error = $"Preferred language must be one of: {string.Join(", ", Languages.Allowed)}." });
            }

            if (request.PreferredAudio is not null && !Audios.Allowed.Contains(request.PreferredAudio))
            {
                return Results.BadRequest(new { error = $"Preferred audio must be one of: {string.Join(", ", Audios.Allowed)}." });
            }

            if (request.Theme is not null && !Themes.Allowed.Contains(request.Theme))
            {
                return Results.BadRequest(new { error = $"Theme must be one of: {string.Join(", ", Themes.Allowed)}." });
            }

            if (request.ColorMode is not null && !Themes.Modes.Contains(request.ColorMode))
            {
                return Results.BadRequest(new { error = $"Color mode must be one of: {string.Join(", ", Themes.Modes)}." });
            }

            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            user.PreferredLanguage = request.PreferredLanguage;
            user.PreferredAudio = request.PreferredAudio;
            user.Theme = request.Theme;
            user.ColorMode = request.ColorMode;
            await users.UpdateAsync(user);
            return Results.Ok(await ToResponseAsync(users, user));
        }).RequireAuthorization();

        app.MapPatch("/api/me/profile", async (UpdateProfileRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 50)
                return Results.BadRequest(new { error = "Display name is required and must be at most 50 characters." });
            if (request.Bio?.Length > 280)
                return Results.BadRequest(new { error = "Bio must be at most 280 characters." });
            user.DisplayName = request.DisplayName.Trim();
            user.ProfileBio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
            user.ProfilePublic = request.ProfilePublic;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(new { error = "Could not update profile visibility." });
        }).RequireAuthorization();
    }

    private static async Task<MeResponse> ToResponseAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        var role = await UserAdmin.RoleOfAsync(users, user);
        return new MeResponse(user.Id, user.Email!, user.DisplayName, role == Roles.Admin, role is not null,
            user.PreferredLanguage, user.PreferredAudio, user.Theme, user.ColorMode,
            user.ProfilePublic, user.ProfileBio, user.AvatarUpdatedAt);
    }
}
