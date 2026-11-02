using System.Security.Claims;
using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniTether.Api.Features.Users;

/// <summary>
/// Account provisioning. There is no public signup: Admins and Managers create
/// accounts here. Every change that affects access (role, lock, password) rotates the
/// target's security stamp, so its existing cookies stop working within the validation
/// interval set in Program.cs.
/// </summary>
public static class UserEndpoints
{
    private const int MaxDisplayName = 50;
    private const string ActorIsAdminKey = "actorIsAdmin";

    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/users")
            .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.Manager))
            .RequireRateLimiting("userAdmin")
            .AddEndpointFilter(async (context, next) =>
            {
                // The cookie's role claims can be up to one validation interval stale, so
                // re-check the actor against the DB: a just-demoted or locked Manager/Admin
                // is refused immediately, and handlers read the fresh answer from Items.
                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var actor = await users.GetUserAsync(context.HttpContext.User);
                var role = actor is null ? null : await UserAdmin.RoleOfAsync(users, actor);
                if (actor is null || role is null || UserAdmin.IsLocked(actor))
                {
                    return Results.Forbid();
                }

                context.HttpContext.Items[ActorIsAdminKey] = role == Roles.Admin;
                return await next(context);
            });

        admin.MapGet("/", async (HttpContext http, UserManager<ApplicationUser> users) =>
        {
            var actorIsAdmin = ActorIsAdmin(http);
            var result = new List<UserResponse>();
            foreach (var user in users.Users.OrderBy(u => u.Email).ToList())
            {
                var role = await UserAdmin.RoleOfAsync(users, user);
                if (UserAdmin.CanManage(actorIsAdmin, role))
                {
                    result.Add(ToResponse(user, role));
                }
            }

            return Results.Ok(result);
        });

        admin.MapPost("/", async (CreateUserRequest request, HttpContext http, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, AppDbContext db) =>
        {
            if (!UserAdmin.CanAssign(ActorIsAdmin(http), request.Role))
            {
                return Results.Forbid();
            }

            if (string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new { error = "Password is required." });
            }

            if (DisplayNameError(request.DisplayName) is { } nameError)
            {
                return Results.BadRequest(new { error = nameError });
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName.Trim(),
            };

            // User + role land together or not at all.
            await using var tx = await db.Database.BeginTransactionAsync();
            if (await users.CreateAsync(user, request.Password) is { Succeeded: false } created)
            {
                return IdentityError(created);
            }

            if (await UserAdmin.SetRoleAsync(users, roles, user, request.Role) is { Succeeded: false } assigned)
            {
                return IdentityError(assigned);
            }

            await tx.CommitAsync();
            return Results.Ok(ToResponse(user, request.Role));
        });

        admin.MapPatch("/{id}", async (string id, UpdateUserRequest request, HttpContext http, UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, AppDbContext db) =>
        {
            var actorIsAdmin = ActorIsAdmin(http);

            // One transaction for role + lock + name + stamp, so a failure part-way leaves
            // nothing half-applied.
            await using var tx = await db.Database.BeginTransactionAsync();
            var user = await users.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var role = await UserAdmin.RoleOfAsync(users, user);
            if (!UserAdmin.CanManage(actorIsAdmin, role) || (request.SetRole && !UserAdmin.CanAssign(actorIsAdmin, request.Role)))
            {
                return Results.Forbid();
            }

            var losesAdmin = role == Roles.Admin && ((request.SetRole && request.Role != Roles.Admin) || request.Locked == true);
            if (losesAdmin)
            {
                // Serializes the last-admin check: two Admins demoting each other at once
                // would otherwise both see one other active Admin and both succeed.
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7001)");
            }

            if (losesAdmin && await UserAdmin.OtherActiveAdminsAsync(users, user) == 0)
            {
                return Results.Conflict(new { error = "This is the last active admin." });
            }

            if (request.DisplayName is not null)
            {
                if (DisplayNameError(request.DisplayName) is { } nameError)
                {
                    return Results.BadRequest(new { error = nameError });
                }

                user.DisplayName = request.DisplayName.Trim();
            }

            if (request.SetRole)
            {
                if (await UserAdmin.SetRoleAsync(users, roles, user, request.Role) is { Succeeded: false } assigned)
                {
                    return IdentityError(assigned);
                }

                role = request.Role;
            }

            if (request.Locked is bool locked)
            {
                user.LockoutEnabled = true;
                user.LockoutEnd = locked ? DateTimeOffset.MaxValue : null;
                user.AccessFailedCount = 0;
            }

            // Also persists DisplayName/lockout: UpdateSecurityStampAsync saves the user.
            if (await users.UpdateSecurityStampAsync(user) is { Succeeded: false } saved)
            {
                return IdentityError(saved);
            }

            await tx.CommitAsync();
            return Results.Ok(ToResponse(user, role));
        });

        admin.MapPost("/{id}/password", async (string id, ResetPasswordRequest request, HttpContext http, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            if (!UserAdmin.CanManage(ActorIsAdmin(http), await UserAdmin.RoleOfAsync(users, user)))
            {
                return Results.Forbid();
            }

            if (string.IsNullOrEmpty(request.Password))
            {
                return Results.BadRequest(new { error = "Password is required." });
            }

            // Validate before touching the hash, so a rejected password never leaves the
            // account with none (RemovePassword + AddPassword isn't atomic).
            foreach (var validator in users.PasswordValidators)
            {
                var valid = await validator.ValidateAsync(users, user, request.Password);
                if (!valid.Succeeded)
                {
                    return IdentityError(valid);
                }
            }

            user.PasswordHash = users.PasswordHasher.HashPassword(user, request.Password);
            return await users.UpdateSecurityStampAsync(user) is { Succeeded: false } saved
                ? IdentityError(saved)
                : Results.NoContent();
        });

        app.MapPost("/api/me/password", async (ChangePasswordRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
            {
                return Results.BadRequest(new { error = "Current and new password are required." });
            }

            var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!changed.Succeeded)
            {
                return IdentityError(changed);
            }

            // ChangePassword rotated the stamp; re-issue this session's cookie so only the
            // user's *other* sessions are signed out.
            await signIn.RefreshSignInAsync(user);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("auth");
    }

    private static bool ActorIsAdmin(HttpContext http) => http.Items[ActorIsAdminKey] is true;

    private static string? DisplayNameError(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Display name is required."
        : name.Trim().Length > MaxDisplayName ? $"Display name must be at most {MaxDisplayName} characters."
        : null;

    /// <summary>A concurrent write to the same user (e.g. its own failed logins) is a 409 the
    /// client can retry; anything else is a validation error.</summary>
    private static IResult IdentityError(IdentityResult result)
    {
        var body = new { error = string.Join(" ", result.Errors.Select(e => e.Description)) };
        return result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
            ? Results.Conflict(body)
            : Results.BadRequest(body);
    }

    private static UserResponse ToResponse(ApplicationUser user, string? role) =>
        new(user.Id, user.Email!, user.DisplayName, role, UserAdmin.IsLocked(user));
}
