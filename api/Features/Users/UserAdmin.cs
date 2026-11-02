using AniTether.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace AniTether.Api.Features.Users;

/// <summary>
/// The one place that decides who may provision or change whom. Every
/// <c>/api/admin/users</c> endpoint goes through <see cref="CanManage"/> and
/// <see cref="CanAssign"/>; nothing else grants or removes a role.
/// </summary>
public static class UserAdmin
{
    /// <summary>An Admin manages anyone. A Manager manages only role-less users — which also
    /// rules out Admins, other Managers and the Manager's own account.</summary>
    public static bool CanManage(bool actorIsAdmin, string? targetRole) =>
        actorIsAdmin || targetRole is null;

    /// <summary>Only an Admin grants a role. A Manager may only create/keep regular users.</summary>
    public static bool CanAssign(bool actorIsAdmin, string? role) =>
        role is null || (actorIsAdmin && Roles.All.Contains(role));

    /// <summary>A user's single role, or null. Admin wins if a row somehow has both.</summary>
    public static async Task<string?> RoleOfAsync(UserManager<ApplicationUser> users, ApplicationUser user)
    {
        var roles = await users.GetRolesAsync(user);
        return Roles.All.FirstOrDefault(roles.Contains);
    }

    public static bool IsLocked(ApplicationUser user) => user.LockoutEnd > DateTimeOffset.UtcNow;

    /// <summary>Replaces the user's role with <paramref name="role"/> (null = none). Returns the
    /// first failure — Identity reports concurrency/validation failures as results, not throws.</summary>
    public static async Task<IdentityResult> SetRoleAsync(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, ApplicationUser user, string? role)
    {
        var current = (await users.GetRolesAsync(user)).Where(Roles.All.Contains).ToList();
        if (current.Count > 0 && await users.RemoveFromRolesAsync(user, current) is { Succeeded: false } removed)
        {
            return removed;
        }

        if (role is null)
        {
            return IdentityResult.Success;
        }

        if (!await roles.RoleExistsAsync(role) && await roles.CreateAsync(new IdentityRole(role)) is { Succeeded: false } createdRole)
        {
            return createdRole;
        }

        return await users.AddToRoleAsync(user, role);
    }

    /// <summary>Unlocked Admins other than <paramref name="except"/> — the last-admin guard.</summary>
    public static async Task<int> OtherActiveAdminsAsync(UserManager<ApplicationUser> users, ApplicationUser except) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => u.Id != except.Id && !IsLocked(u));

    /// <summary>
    /// First-run admin: when no Admin exists and <c>Bootstrap__AdminEmail</c> +
    /// <c>Bootstrap__AdminPassword</c> are set, create that user (or promote it if the email
    /// already has an account, keeping its password). No-op once any Admin exists.
    /// </summary>
    public static async Task BootstrapAdminAsync(IServiceProvider services, IConfiguration config)
    {
        var email = config["Bootstrap:AdminEmail"];
        var password = config["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UserAdmin));

        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, DisplayName = email.Split('@')[0] };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                log.LogError("Bootstrap admin {Email} could not be created: {Errors}", email, string.Join(" ", created.Errors.Select(e => e.Description)));
                return;
            }
        }

        var promoted = await SetRoleAsync(users, roles, user, Roles.Admin);
        if (!promoted.Succeeded)
        {
            log.LogError("Bootstrap admin {Email} could not be made Admin: {Errors}", email, string.Join(" ", promoted.Errors.Select(e => e.Description)));
            return;
        }

        await users.UpdateSecurityStampAsync(user);
        log.LogWarning("Bootstrap admin {Email} created. Remove Bootstrap__AdminPassword from the environment now.", email);
    }
}
