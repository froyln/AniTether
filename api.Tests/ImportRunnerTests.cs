using AniTether.Api.Data;
using AniTether.Api.Features.Anime;
using AniTether.Api.Features.Import;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AniTether.Api.Tests;

/// <summary>
/// Exercises ImportRunner.ApplyAsync against a real Postgres (via WebApplicationFactory's
/// DbContext, same as the other Testcontainers-backed tests), with AniListMedia built by
/// hand instead of a real AniListClient call — proves the DB-level merge/scoping logic
/// without depending on the live AniList network (untested elsewhere in this codebase).
/// </summary>
[Collection(PostgresCollection.Name)]
public class ImportRunnerTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = null!;

    public ImportRunnerTests(PostgresFixture postgres) => _postgres = postgres;

    private WebApplicationFactory<Program>? _factory;
    private string _userA = null!;
    private string _userB = null!;

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

        _userA = await CreateUserAsync("a@test.local");
        _userB = await CreateUserAsync("b@test.local");
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private async Task<string> CreateUserAsync(string email)
    {
        using var scope = _factory!.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email };
        var result = await users.CreateAsync(user, "Passw0rd!");
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return user.Id;
    }

    private static AniListMedia Media(int id, string title, int episodes) =>
        new(id, null, new AniListTitle(title, null), null, null, null, null, episodes, null, null, null);

    private AppDbContext NewDb() => _factory!.Services.CreateScope().ServiceProvider.GetRequiredService<AppDbContext>();

    [Fact]
    public async Task ApplyAsync_NewAnime_CreatesAnimeAndEntry()
    {
        using var db = NewDb();
        var items = new List<ImportItem> { new(Media(1, "Test Anime", 24), ListStatus.Watching, 5, 8, null, null) };

        var summary = await ImportRunner.ApplyAsync(db, _userA, items, CancellationToken.None);

        Assert.Equal(1, summary.Added);
        var entry = await db.ListEntries.SingleAsync(e => e.UserId == _userA && e.AnilistId == 1);
        Assert.Equal(5, entry.Progress);
        Assert.Equal(ListStatus.Watching, entry.Status);
        Assert.NotNull(await db.Anime.FindAsync(1));
    }

    [Fact]
    public async Task ApplyAsync_LocalProgressAhead_Skips()
    {
        using var seedDb = NewDb();
        await ImportRunner.ApplyAsync(seedDb, _userA, [new(Media(2, "Ahead Anime", 24), ListStatus.Watching, 20, null, null, null)], CancellationToken.None);

        using var db = NewDb();
        var summary = await ImportRunner.ApplyAsync(db, _userA, [new(Media(2, "Ahead Anime", 24), ListStatus.Dropped, 3, null, null, null)], CancellationToken.None);

        Assert.Equal(0, summary.Added);
        Assert.Equal(1, summary.Skipped);
        var entry = await db.ListEntries.SingleAsync(e => e.UserId == _userA && e.AnilistId == 2);
        Assert.Equal(20, entry.Progress);
        Assert.Equal(ListStatus.Watching, entry.Status);
    }

    [Fact]
    public async Task ApplyAsync_AdvancingImportWritesTheNewRevisionAndSnapshot()
    {
        using var db = NewDb();
        await ImportRunner.ApplyAsync(db, _userA, [new(Media(4, "Imported Anime", 24), ListStatus.Watching, 5, null, null, null)], CancellationToken.None);

        var summary = await ImportRunner.ApplyAsync(db, _userA, [new(Media(4, "Imported Anime", 24), ListStatus.Completed, 24, 9, null, null)], CancellationToken.None);

        Assert.Equal(1, summary.Updated);
        var entry = await db.ListEntries.SingleAsync(e => e.UserId == _userA && e.AnilistId == 4);
        Assert.Equal(2, entry.Revision);
        Assert.Null(entry.ManualProgressRevision);
        var changes = await db.SyncChanges.Where(change => change.UserId == _userA).OrderBy(change => change.Cursor).ToListAsync();
        Assert.Equal([1L, 2L], changes.Select(change => change.Cursor));
        using var snapshot = JsonDocument.Parse(changes[1].SnapshotJson!);
        Assert.Equal(24, snapshot.RootElement.GetProperty("progress").GetInt32());
        Assert.Equal("Completed", snapshot.RootElement.GetProperty("status").GetString());
        Assert.Equal("2", snapshot.RootElement.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task ApplyAsync_ForOneUser_NeverCreatesOrTouchesAnotherUsersEntry()
    {
        using var seedDb = NewDb();
        await ImportRunner.ApplyAsync(seedDb, _userA, [new(Media(3, "Shared Anime", 24), ListStatus.Completed, 24, 10, null, null)], CancellationToken.None);

        using var db = NewDb();
        var summary = await ImportRunner.ApplyAsync(db, _userB, [new(Media(3, "Shared Anime", 24), ListStatus.PlanToWatch, 0, null, null, null)], CancellationToken.None);

        Assert.Equal(1, summary.Added);

        var entryA = await db.ListEntries.SingleAsync(e => e.UserId == _userA && e.AnilistId == 3);
        Assert.Equal(24, entryA.Progress);
        Assert.Equal(ListStatus.Completed, entryA.Status);

        var entryB = await db.ListEntries.SingleAsync(e => e.UserId == _userB && e.AnilistId == 3);
        Assert.Equal(0, entryB.Progress);
        Assert.Equal(ListStatus.PlanToWatch, entryB.Status);
    }
}
