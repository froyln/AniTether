using System.Net.Http.Json;
using AniTether.Api.Data;
using AniTether.Api.Features.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AniTether.Api.Tests;

/// <summary>There is no public signup (Task 7r): tests create accounts directly and log in
/// through the real login endpoint.</summary>
public static class TestUsers
{
    public const string Password = "Passw0rd!";

    public static async Task<HttpClient> CreateAndLoginAsync(WebApplicationFactory<Program> factory, string email, string? role = null)
    {
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
            var created = await users.CreateAsync(user, Password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
            }

            var assigned = await UserAdmin.SetRoleAsync(users, roles, user, role);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", assigned.Errors.Select(e => e.Description)));
            }
        }

        return await LoginAsync(factory, email);
    }

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email, string password = Password)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        return client;
    }
}
