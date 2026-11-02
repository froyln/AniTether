using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AniTether.Api.Data;
using AniTether.Api.Features.Sources;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SourceHealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public SourceHealthEndpointTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;
    private Guid _sourceId;

    public async Task InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        db.Anime.Add(new Anime { AnilistId = 154587, TitleRomaji = "Sousou no Frieren", MalId = 553 });
        var source = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Test Source",
            BaseUrl = "https://example.com",
            SearchUrlTemplate = "https://example.com/search?q={title}",
            Enabled = true,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        _sourceId = source.Id;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private Task<HttpClient> SignUpAsync(string email) => TestUsers.CreateAndLoginAsync(_factory!, email);

    private Task<HttpClient> SignUpAdminAsync(string email) => TestUsers.CreateAndLoginAsync(_factory!, email, Roles.Admin);

    [Fact]
    public async Task HealthReport_Anonymous_Returns401()
    {
        var response = await _factory!.CreateClient().GetAsync("/api/admin/sources/health");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthReport_NonAdmin_Returns403()
    {
        var client = await SignUpAsync("user@test.local");
        var response = await client.GetAsync("/api/admin/sources/health");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HealthReport_Admin_SeesEverySource_HealthyUntilACheckFails()
    {
        var client = await SignUpAdminAsync("admin@test.local");

        var before = Json(await client.GetAsync("/api/admin/sources/health"));
        Assert.Equal(JsonValueKind.False, before.GetProperty("running").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("lastRunAt").ValueKind);
        var source = TestSource(before);
        Assert.Equal("Test Source", source.GetProperty("name").GetString());
        Assert.True(source.GetProperty("healthy").GetBoolean());
        Assert.Empty(source.GetProperty("checks").EnumerateArray());

        await AddChecksAsync(broken: 2);

        var after = Json(await client.GetAsync("/api/admin/sources/health"));
        var updated = TestSource(after);
        Assert.True(updated.GetProperty("healthy").GetBoolean()); // two failures isn't enough yet
        var check = updated.GetProperty("checks").EnumerateArray().Single();
        Assert.Equal(SourceCheckKinds.Site, check.GetProperty("kind").GetString());
        Assert.Equal(CheckOutcome.Broken, check.GetProperty("result").GetString());
        Assert.Equal(522, check.GetProperty("httpStatus").GetInt32());
        Assert.Equal(2, check.GetProperty("consecutiveFailures").GetInt32());
    }

    [Fact]
    public async Task UnhealthySource_IsHiddenFromUsers_ButNotFromAdmins()
    {
        await AddChecksAsync(broken: SourceHealth.HideAfterFailures);
        var admin = await SignUpAdminAsync("admin@test.local");
        var user = await SignUpAsync("user@test.local");

        var forUsers = Json(await user.GetAsync("/api/sources"));
        Assert.DoesNotContain(forUsers.EnumerateArray(), s => s.GetProperty("id").GetGuid() == _sourceId);

        var forAdmins = Json(await admin.GetAsync("/api/sources/all"));
        var source = forAdmins.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == _sourceId);
        Assert.False(source.GetProperty("healthy").GetBoolean());
        Assert.True(source.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task UnknownCheck_NeverHidesASource()
    {
        // A bot-blocked answer is not evidence the site is broken.
        await AddChecksAsync(broken: 0, unknown: 9);
        var user = await SignUpAsync("user@test.local");

        var sources = Json(await user.GetAsync("/api/sources"));
        Assert.Contains(sources.EnumerateArray(), s => s.GetProperty("id").GetGuid() == _sourceId);
    }

    [Fact]
    public async Task UserReport_StoresIt_AndShowsItToAdmins()
    {
        var user = await SignUpAsync("user@test.local");
        var response = await user.PostAsJsonAsync($"/api/sources/{_sourceId}/report", new { anilistId = 154587, episode = 3, url = "https://example.com/watch/x" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var admin = await SignUpAdminAsync("admin@test.local");
        var report = Json(await admin.GetAsync("/api/admin/sources/health")).GetProperty("reports").EnumerateArray().Single();
        Assert.Equal("Test Source", report.GetProperty("sourceName").GetString());
        Assert.Equal(154587, report.GetProperty("anilistId").GetInt32());
        Assert.Equal(3, report.GetProperty("episode").GetInt32());
        Assert.Equal("https://example.com/watch/x", report.GetProperty("url").GetString());
        Assert.Equal("Sousou no Frieren", report.GetProperty("animeTitle").GetString());
    }

    [Fact]
    public async Task UserReport_NeverStoresANonHttpsUrl()
    {
        // Rendered as a link on the report page, so only a real absolute https:// URL is kept.
        var user = await SignUpAsync("user@test.local");
        await user.PostAsJsonAsync($"/api/sources/{_sourceId}/report", new { anilistId = 154587, episode = 1, url = "javascript:alert(1)" });

        var admin = await SignUpAdminAsync("admin@test.local");
        Assert.Equal("", Json(await admin.GetAsync("/api/admin/sources/health")).GetProperty("reports").EnumerateArray().Single().GetProperty("url").GetString());
    }

    [Fact]
    public async Task UserReport_Anonymous_Returns401()
    {
        var response = await _factory!.CreateClient().PostAsJsonAsync($"/api/sources/{_sourceId}/report", new { anilistId = 154587 });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserReport_UnknownSource_Returns404()
    {
        var user = await SignUpAsync("user@test.local");
        var response = await user.PostAsJsonAsync($"/api/sources/{Guid.NewGuid()}/report", new { anilistId = 154587 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CheckNow_NonAdmin_Returns403()
    {
        var client = await SignUpAsync("user@test.local");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/admin/sources/check", null)).StatusCode);
    }

    [Fact]
    public async Task AlertRecipients_SkipLockedAdmins()
    {
        await SignUpAdminAsync("active-admin@test.local");
        await SignUpAdminAsync("removed-admin@test.local");
        await SignUpAsync("user@test.local");

        using var scope = _factory!.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var removed = (await users.FindByEmailAsync("removed-admin@test.local"))!;
        removed.LockoutEnabled = true;
        removed.LockoutEnd = DateTimeOffset.MaxValue;
        await users.UpdateAsync(removed);

        Assert.Equal(["active-admin@test.local"], await SourceHealthService.AlertRecipientsAsync(users));
    }

    private async Task AddChecksAsync(int broken, int unknown = 0)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SourceChecks.Add(new SourceCheck
        {
            SourceId = _sourceId,
            Kind = SourceCheckKinds.Site,
            Result = broken > 0 ? CheckOutcome.Broken : CheckOutcome.Unknown,
            HttpStatus = broken > 0 ? 522 : 503,
            Detail = "test",
            CheckedAt = DateTimeOffset.UtcNow,
            ConsecutiveFailures = broken > 0 ? broken : unknown,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>The seeded migrations add their own sources to the test database, so
    /// every assertion here looks for this fixture's row by id rather than assuming the
    /// table has exactly one row.</summary>
    private JsonElement TestSource(JsonElement report) =>
        report.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == _sourceId);

    private static JsonElement Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(response.Content.ReadAsStringAsync().Result).RootElement;
    }
}
