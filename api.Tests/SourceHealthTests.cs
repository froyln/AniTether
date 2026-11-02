using System.Net.Http;
using AniTether.Api.Data;
using AniTether.Api.Features.Sources;
using Xunit;

namespace AniTether.Api.Tests;

/// <summary>The rules the admin report trusts, with no network and no container: a site's
/// behaviour changing must never be able to break these.</summary>
public class SourceHealthTests
{
    // Some sites answer a non-browser client a 404 "Content Removed" page behind
    // Cloudflare for a URL that plays fine in a real browser.
    private const string BlockPage = "<html><head><title>Content Removed</title></head><body>...</body></html>";

    [Fact]
    public void BlockPage_Detected_ByTitle()
    {
        Assert.True(SourceHealth.LooksBlocked(BlockPage));
        Assert.False(SourceHealth.LooksBlocked("<html><body>Frieren episode 1</body></html>"));
    }

    [Theory]
    [InlineData(200, "ok")]
    [InlineData(301, "ok")]
    public void Site_Reachable_IsOk(int status, string expected) =>
        Assert.Equal(expected, SourceHealth.Site(status, blocked: false, movedTo: null).Result);

    [Fact]
    public void Site_ConnectionFailure_IsBroken()
    {
        var outcome = SourceHealth.Site(null, blocked: false, movedTo: null);
        Assert.Equal(CheckOutcome.Broken, outcome.Result);
        Assert.Contains("No response", outcome.Detail);
    }

    [Fact]
    public void Site_ServerError_IsBroken() =>
        // An origin 522 — the exact case this classification exists for.
        Assert.Equal(CheckOutcome.Broken, SourceHealth.Site(522, blocked: false, movedTo: null).Result);

    [Fact]
    public void Site_MovedHost_IsBrokenWithHostInHint()
    {
        var outcome = SourceHealth.Site(301, blocked: false, movedTo: "new.example.com");
        Assert.Equal(CheckOutcome.Broken, outcome.Result);
        Assert.Contains("new.example.com", outcome.Detail);
        Assert.Contains("new.example.com", outcome.Hint);
    }

    [Fact]
    public void Site_BotBlocked_IsUnknown_NotBroken() =>
        // A Cloudflare challenge is not evidence the site is broken — no false auto-hide.
        Assert.Equal(CheckOutcome.Unknown, SourceHealth.Site(503, blocked: true, movedTo: null).Result);

    [Fact]
    public void Page_404_IsBroken_UrlFormatChanged()
    {
        var outcome = SourceHealth.Page(404, blocked: false, movedTo: null, "ok", "paste a working URL");
        Assert.Equal(CheckOutcome.Broken, outcome.Result);
        Assert.Equal("paste a working URL", outcome.Hint);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(503)]
    public void Page_ChallengeStatus_IsUnknown(int status) =>
        Assert.Equal(CheckOutcome.Unknown, SourceHealth.Page(status, blocked: false, movedTo: null, "ok", "hint").Result);

    [Fact]
    public void Page_200_IsOk() => Assert.Equal(CheckOutcome.Ok, SourceHealth.Page(200, blocked: false, movedTo: null, "works", "hint").Result);

    [Fact]
    public void Embed_FrameBlockingHeader_IsBroken()
    {
        // Many sites answer this; the in-site player can't be used for them.
        var outcome = SourceHealth.Embed(200, blocked: false, "SAMEORIGIN", null);
        Assert.Equal(CheckOutcome.Broken, outcome.Result);
        Assert.Contains("X-Frame-Options", outcome.Detail);
    }

    [Fact]
    public void Embed_FrameAncestorsDirective_IsBroken() =>
        Assert.Equal(CheckOutcome.Broken, SourceHealth.Embed(200, blocked: false, null, "'none'").Result);

    [Fact]
    public void Embed_NoBlockingHeaders_IsOk_ButSaysItCannotConfirmPlayback()
    {
        var outcome = SourceHealth.Embed(200, blocked: false, null, null);
        Assert.Equal(CheckOutcome.Ok, outcome.Result);
        Assert.Contains("can't confirm", outcome.Detail);
    }

    [Fact]
    public void Embed_BlockPage_IsUnknown_NeverBroken()
    {
        // The regression this classification exists for: a 404 "Content Removed"
        // must not read as a dead embed, or three runs later a working player is hidden.
        Assert.True(SourceHealth.LooksBlocked(BlockPage));
        var outcome = SourceHealth.Embed(404, blocked: SourceHealth.LooksBlocked(BlockPage), null, null);
        Assert.Equal(CheckOutcome.Unknown, outcome.Result);
    }

    [Fact]
    public void Embed_AmbiguousStatus_IsUnknown() =>
        Assert.Equal(CheckOutcome.Unknown, SourceHealth.Embed(404, blocked: false, null, null).Result);

    [Fact]
    public void Alert_OnlyOnStateChange()
    {
        var broken = new CheckOutcome(CheckOutcome.Broken, 500, "down");
        var ok = new CheckOutcome(CheckOutcome.Ok, 200, "back");
        var unknown = new CheckOutcome(CheckOutcome.Unknown, 503, "blocked");

        Assert.Equal("broken", SourceHealth.AlertFor(CheckOutcome.Ok, broken, 1));
        Assert.Null(SourceHealth.AlertFor(CheckOutcome.Broken, broken, 2)); // still broken, nothing new
        Assert.Equal("recovered", SourceHealth.AlertFor(CheckOutcome.Broken, ok, 0));
        Assert.Null(SourceHealth.AlertFor(CheckOutcome.Ok, ok, 0));
        Assert.Null(SourceHealth.AlertFor(CheckOutcome.Broken, unknown, 3)); // unknown never emails
        Assert.Null(SourceHealth.AlertFor(CheckOutcome.Ok, unknown, 0));
    }

    [Fact]
    public void ApplyOutcome_CountsOnlyConsecutiveBreaks_AndOkResets()
    {
        var check = new SourceCheck { SourceId = Guid.NewGuid(), Kind = SourceCheckKinds.Site, Result = CheckOutcome.Ok };
        var t0 = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var broken = new CheckOutcome(CheckOutcome.Broken, 522, "down");

        SourceHealth.ApplyOutcome(check, broken, t0);
        Assert.Equal(1, check.ConsecutiveFailures);
        Assert.Equal(t0, check.FailingSince);

        // An unknown run (bot-blocked) must not count, and must not clear the streak.
        SourceHealth.ApplyOutcome(check, new(CheckOutcome.Unknown, 503, "blocked"), t0.AddHours(1));
        Assert.Equal(1, check.ConsecutiveFailures);
        Assert.Equal(t0, check.FailingSince);

        SourceHealth.ApplyOutcome(check, broken, t0.AddHours(2));
        SourceHealth.ApplyOutcome(check, broken, t0.AddHours(3));
        Assert.Equal(3, check.ConsecutiveFailures);
        Assert.True(SourceHealth.IsHidden(check));

        SourceHealth.ApplyOutcome(check, new(CheckOutcome.Ok, 200, "back"), t0.AddHours(4));
        Assert.Equal(0, check.ConsecutiveFailures);
        Assert.Null(check.FailingSince);
        Assert.False(SourceHealth.IsHidden(check));
    }

    [Fact]
    public void IsHidden_OnlyForTheKindsAWatchButtonDependsOn()
    {
        var link = new SourceCheck { Kind = SourceCheckKinds.Links, Result = CheckOutcome.Broken, ConsecutiveFailures = 9 };
        var search = new SourceCheck { Kind = SourceCheckKinds.Search, Result = CheckOutcome.Broken, ConsecutiveFailures = 9 };
        var site = new SourceCheck { Kind = SourceCheckKinds.Site, Result = CheckOutcome.Broken, ConsecutiveFailures = SourceHealth.HideAfterFailures };

        Assert.True(SourceHealth.IsHidden(site));
        Assert.False(SourceHealth.IsHidden(link));   // a stale link isn't a dead site
        Assert.False(SourceHealth.IsHidden(search));
    }

    [Fact]
    public void IsHidden_NotBeforeThreeFailures()
    {
        var check = new SourceCheck { Kind = SourceCheckKinds.Episode, Result = CheckOutcome.Broken, ConsecutiveFailures = SourceHealth.HideAfterFailures - 1 };
        Assert.False(SourceHealth.IsHidden(check));
    }

    [Fact]
    public async Task Episode_404ForOneAnimeButTheFormatStillWorks_IsUnknown_NotBroken()
    {
        // A site may 404 for one show while the same /watch/{anilist_id} URL still works
        // for another (it just doesn't carry the first). Reading the first 404 as "URL
        // format changed" would hide a working source after three runs.
        var probe = new SourceHealthProbe(FakeHttp.For((uri, _) =>
            uri.ToString().Contains("154587") ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.MovedPermanently));
        var source = new Source
        {
            Id = Guid.NewGuid(),
            Name = "Example",
            BaseUrl = "https://www.example.com",
            SearchUrlTemplate = "https://www.example.com/search?query={title}",
            EpisodeUrlTemplate = "https://www.example.com/watch/{anilist_id}?ep={episode}",
        };

        var outcomes = await probe.ProbeAsync(
            source, new SourceLinkResolver(FakeHttp.Never()),
            new ProbeData(154587, "Sousou no Frieren", 52991, null, []),
            new ProbeData(196187, "One Piece", 21, null, []),
            CancellationToken.None);

        Assert.Equal(CheckOutcome.Unknown, outcomes[SourceCheckKinds.Episode].Result);
        Assert.Contains("same URL format works", outcomes[SourceCheckKinds.Episode].Detail);
    }

    [Fact]
    public async Task Episode_404ForBothReferenceAnime_IsBroken_UrlFormatChanged()
    {
        var probe = new SourceHealthProbe(FakeHttp.For((_, _) => System.Net.HttpStatusCode.NotFound));
        var source = new Source
        {
            Id = Guid.NewGuid(),
            Name = "X",
            BaseUrl = "https://example.com",
            SearchUrlTemplate = "https://example.com/search?q={title}",
            EpisodeUrlTemplate = "https://example.com/watch/{anilist_id}/{episode}",
        };

        var outcomes = await probe.ProbeAsync(
            source, new SourceLinkResolver(FakeHttp.Never()),
            new ProbeData(154587, "Frieren", null, null, []),
            new ProbeData(196187, "One Piece", null, null, []),
            CancellationToken.None);

        Assert.Equal(CheckOutcome.Broken, outcomes[SourceCheckKinds.Episode].Result);
    }

    [Fact]
    public void TryBuildUrl_MissingSlug_IsNotBuildable()
    {
        // {slug} with nothing saved: unknown, never a false "broken". Filling it with an
        // empty string would still produce a valid-looking URL that 404s.
        var data = new ProbeData(154587, "Sousou no Frieren", null, null, []);
        Assert.False(SourceUrlValidation.TryBuildUrl("https://example.com/anime/{slug}/ep-{episode}", data.AnilistId, data.MalId, data.Slug, 1, out _, out var reason));
        Assert.Contains("per-anime link", reason);
    }

    [Fact]
    public void TryBuildUrl_MissingMalId_IsNotBuildable()
    {
        var built = SourceUrlValidation.TryBuildUrl(
            "https://example.com/mal/{mal_id}/ep-{episode}",
            154587, null, "slug", 1,
            out _,
            out var reason);
        Assert.False(built);
        Assert.Contains("MyAnimeList", reason);
    }

    [Fact]
    public void TryBuildUrl_UnknownPlaceholder_IsNotBuildable()
    {
        var data = new ProbeData(154587, "Frieren", null, "slug", []);
        Assert.False(SourceUrlValidation.TryBuildUrl("https://example.com/{whatever}", data.AnilistId, data.MalId, data.Slug, 1, out _, out _));
    }

    [Fact]
    public void TryBuildUrl_FillsEveryKnownPlaceholder()
    {
        var data = new ProbeData(154587, "Frieren", 553, "sousou-no-frieren", []);
        Assert.True(SourceUrlValidation.TryBuildUrl(
            "https://example.com/{anilist_id}/{mal_id}/{slug}/{episode}/{audio}",
            data.AnilistId, data.MalId, data.Slug, 1,
            out var url,
            out _));
        Assert.Equal("https://example.com/154587/553/sousou-no-frieren/1/sub", url!.ToString());
    }

    [Fact]
    public void Page_SameHostRedirect_IsOk() =>
        // A site's homepage and a canonical episode URL both redirect on-site; that's
        // healthy, not "URL format changed". Only leaving the host is death.
        Assert.Equal(CheckOutcome.Ok, SourceHealth.Page(302, blocked: false, movedTo: null, "ok", "hint").Result);

    [Fact]
    public void Page_OffHostRedirect_IsBroken() =>
        Assert.Equal(CheckOutcome.Broken, SourceHealth.Page(301, blocked: false, movedTo: "https://other.example/x", "ok", "hint").Result);
}

/// <summary>Minimal HttpClient stand-ins: the probe only ever calls SendAsync.</summary>
internal static class FakeHttp
{
    public static HttpClient For(Func<Uri, HttpRequestMessage, System.Net.HttpStatusCode> status) =>
        new(new Handler(status)) { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>For the resolver's search check, which this test doesn't care about.</summary>
    public static HttpClient Never() => new(new Handler((_, _) => System.Net.HttpStatusCode.NotFound));

    private sealed class Handler(Func<Uri, HttpRequestMessage, System.Net.HttpStatusCode> status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(status(request.RequestUri!, request))
            {
                Content = new StringContent("<html><body>ok</body></html>", System.Text.Encoding.UTF8, "text/html"),
            });
        }
    }
}
