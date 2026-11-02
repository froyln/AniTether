using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniTether.Api.Data;
using AniTether.Api.Features.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class UserAdminEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public UserAdminEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            // Validate the stamp on every request so revocation is observable without waiting.
            builder.ConfigureServices(s => s.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero));
        });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private Task<HttpClient> As(string email, string? role = null) => TestUsers.CreateAndLoginAsync(_factory!, email, role);

    private static async Task<string> CreateAsync(HttpClient client, string email, string? role = null)
    {
        var response = await client.PostAsJsonAsync("/api/admin/users", new { email, displayName = email, password = TestUsers.Password, role });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<string> IdOf(string email)
    {
        using var scope = _factory!.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    [Fact]
    public async Task Signup_IsGone()
    {
        var response = await _factory!.CreateClient().PostAsJsonAsync("/api/auth/signup", new { email = "x@test.local", password = TestUsers.Password, displayName = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RegularUser_CannotUseUserAdmin()
    {
        var user = await As("user@test.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/users")).StatusCode);
        var create = await user.PostAsJsonAsync("/api/admin/users", new { email = "n@test.local", displayName = "n", password = TestUsers.Password });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Manager_CreatesRegularUser_WhoCanLogIn()
    {
        var manager = await As("manager@test.local", Roles.Manager);

        await CreateAsync(manager, "new@test.local");

        var me = await (await TestUsers.LoginAsync(_factory!, "new@test.local")).GetFromJsonAsync<JsonElement>("/api/me");
        Assert.False(me.GetProperty("canManageUsers").GetBoolean());
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Manager)]
    public async Task Manager_CannotCreatePrivilegedUser(string role)
    {
        var manager = await As("manager@test.local", Roles.Manager);

        var response = await manager.PostAsJsonAsync("/api/admin/users", new { email = "evil@test.local", displayName = "evil", password = TestUsers.Password, role });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _factory!.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("evil@test.local"));
    }

    [Fact]
    public async Task Manager_CannotPromoteRegularUser()
    {
        var manager = await As("manager@test.local", Roles.Manager);
        var id = await CreateAsync(manager, "u@test.local");

        var response = await manager.PatchAsJsonAsync($"/api/admin/users/{id}", new { setRole = true, role = Roles.Admin });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_CannotTouchAdmin_OrOwnAccount()
    {
        await As("admin@test.local", Roles.Admin);
        var manager = await As("manager@test.local", Roles.Manager);
        var adminId = await IdOf("admin@test.local");
        var selfId = await IdOf("manager@test.local");

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PatchAsJsonAsync($"/api/admin/users/{adminId}", new { locked = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/admin/users/{adminId}/password", new { password = "Hijack3d!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PatchAsJsonAsync($"/api/admin/users/{selfId}", new { setRole = true, role = Roles.Admin })).StatusCode);
    }

    [Fact]
    public async Task Manager_ListsOnlyRegularUsers()
    {
        await As("admin@test.local", Roles.Admin);
        var manager = await As("manager@test.local", Roles.Manager);
        await CreateAsync(manager, "u@test.local");

        var list = await manager.GetFromJsonAsync<JsonElement>("/api/admin/users");

        Assert.Equal(["u@test.local"], list.EnumerateArray().Select(u => u.GetProperty("email").GetString()));
    }

    [Fact]
    public async Task Create_MissingPassword_Returns400()
    {
        var admin = await As("admin@test.local", Roles.Admin);

        var response = await admin.PostAsJsonAsync("/api/admin/users", new { email = "n@test.local", displayName = "n" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateAdmin()
    {
        var admin = await As("admin@test.local", Roles.Admin);

        await CreateAsync(admin, "admin2@test.local", Roles.Admin);

        var me = await (await TestUsers.LoginAsync(_factory!, "admin2@test.local")).GetFromJsonAsync<JsonElement>("/api/me");
        Assert.True(me.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task LastAdmin_CannotBeDemotedOrLocked()
    {
        var admin = await As("admin@test.local", Roles.Admin);
        var id = await IdOf("admin@test.local");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/users/{id}", new { setRole = true, role = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/users/{id}", new { locked = true })).StatusCode);
    }

    [Fact]
    public async Task RoleChange_RevokesOldCookie_NewLoginHasRole()
    {
        var admin = await As("admin@test.local", Roles.Admin);
        var user = await As("user@test.local");

        var patch = await admin.PatchAsJsonAsync($"/api/admin/users/{await IdOf("user@test.local")}", new { setRole = true, role = Roles.Manager });
        patch.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/me")).StatusCode);
        var me = await (await TestUsers.LoginAsync(_factory!, "user@test.local")).GetFromJsonAsync<JsonElement>("/api/me");
        Assert.True(me.GetProperty("canManageUsers").GetBoolean());
    }

    [Fact]
    public async Task Lock_RevokesSession_AndBlocksLogin()
    {
        var admin = await As("admin@test.local", Roles.Admin);
        var user = await As("user@test.local");

        (await admin.PatchAsJsonAsync($"/api/admin/users/{await IdOf("user@test.local")}", new { locked = true })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/me")).StatusCode);
        var login = await _factory!.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = "user@test.local", password = TestUsers.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Login_WithMissingFields_Is400Not500()
    {
        var login = await _factory!.CreateClient().PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
    }

    [Fact]
    public async Task AdminReset_RejectsWeakPassword_WithoutClearingTheOldOne()
    {
        var admin = await As("admin@test.local", Roles.Admin);
        await As("user@test.local");
        var id = await IdOf("user@test.local");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{id}/password", new { password = "weak" })).StatusCode);
        await TestUsers.LoginAsync(_factory!, "user@test.local");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{id}/password", new { password = "N3wPassw0rd!" })).StatusCode);
        await TestUsers.LoginAsync(_factory!, "user@test.local", "N3wPassw0rd!");
    }

    [Fact]
    public async Task ChangeOwnPassword_RequiresCurrentPassword()
    {
        var user = await As("user@test.local");

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/me/password", new { currentPassword = "wrong", newPassword = "N3wPassw0rd!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync("/api/me/password", new { currentPassword = TestUsers.Password, newPassword = "N3wPassw0rd!" })).StatusCode);
        await TestUsers.LoginAsync(_factory!, "user@test.local", "N3wPassw0rd!");
    }

    [Fact]
    public async Task Bootstrap_CreatesOneAdmin_AndIsIdempotent()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bootstrap:AdminEmail"] = "owner@test.local",
            ["Bootstrap:AdminPassword"] = TestUsers.Password,
        }).Build();

        await UserAdmin.BootstrapAdminAsync(_factory!.Services, config);
        await UserAdmin.BootstrapAdminAsync(_factory.Services, config);

        using var scope = _factory.Services.CreateScope();
        var admins = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetUsersInRoleAsync(Roles.Admin);
        Assert.Equal(["owner@test.local"], admins.Select(a => a.Email));
    }
}
