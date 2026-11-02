using AniTether.Api.Features.Sources;
using Xunit;

namespace AniTether.Api.Tests;

public class SourceLinkValidationTests
{
    [Fact]
    public void TryParseHttpsUrl_AcceptsAbsoluteHttps()
    {
        var ok = SourceUrlValidation.TryParseHttpsUrl("https://example.com/anime/frieren", out var uri);

        Assert.True(ok);
        Assert.Equal("example.com", uri.Host);
    }

    [Fact]
    public void TryParseHttpsUrl_RejectsJavascriptScheme()
    {
        var ok = SourceUrlValidation.TryParseHttpsUrl("javascript:alert(1)", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParseHttpsUrl_RejectsHttp()
    {
        var ok = SourceUrlValidation.TryParseHttpsUrl("http://example.com/anime/frieren", out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("https://user@example.com/anime/frieren")]
    [InlineData("https://example.com:8443/anime/frieren")]
    [InlineData("https://example.com:444/anime/frieren")]
    public void TryParseHttpsUrl_RejectsCredentialsAndNonDefaultPorts(string value)
    {
        Assert.False(SourceUrlValidation.TryParseHttpsUrl(value, out _));
    }

    [Fact]
    public void HostMatches_RejectsDifferentHost()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://evil.example/anime/frieren", out var uri);

        Assert.False(SourceUrlValidation.HostMatches(uri, "https://example.com"));
    }

    [Fact]
    public void HostMatches_AcceptsSameHost()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://example.com/anime/frieren", out var uri);

        Assert.True(SourceUrlValidation.HostMatches(uri, "https://example.com"));
    }

    [Fact]
    public void DeriveSlug_TakesLastPathSegment_TrailingSlashIgnored()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://example.com/anime/sousou-no-frieren/", out var uri);

        Assert.Equal("sousou-no-frieren", SourceUrlValidation.DeriveSlug(uri));
    }

    [Fact]
    public void DeriveSlug_DecodesUrlEncodedSegment()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://example.com/anime/attack%20on%20titan", out var uri);

        Assert.Equal("attack on titan", SourceUrlValidation.DeriveSlug(uri));
    }

    [Fact]
    public void DeriveSlug_IgnoresQueryString()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://example.com/anime/frieren?ep=6", out var uri);

        Assert.Equal("frieren", SourceUrlValidation.DeriveSlug(uri));
    }

    [Fact]
    public void DeriveSlug_ReturnsNullForBareDomain()
    {
        SourceUrlValidation.TryParseHttpsUrl("https://example.com", out var uri);

        Assert.Null(SourceUrlValidation.DeriveSlug(uri));
    }

    [Theory]
    [InlineData("https://example.com/anime/sousou-no-frieren", "https://example.com/anime/{slug}/episode/{episode}")]
    [InlineData("https://example.com/anime/sousou-no-frieren/episode/3", "https://example.com/anime/{slug}/episode/{episode}")]
    [InlineData("https://example.net/watch/sousou-no-frieren/ep-1", "https://example.net/watch/{slug}/ep-{episode}")]
    [InlineData("https://example.org/anime/sousou-no-frieren/5", "https://example.org/anime/{slug}/{episode}")]
    public void DeriveSlug_WithTemplate_TakesSlugPosition_FromAnimeOrEpisodeUrl(string url, string template)
    {
        Assert.Equal("sousou-no-frieren", SourceUrlValidation.DeriveSlug(new Uri(url), template));
    }

    [Fact]
    public void DeriveSlug_TemplatePrefixMismatch_FallsBackToLastSegment()
    {
        var uri = new Uri("https://example.org/browse/sousou-no-frieren");

        Assert.Equal("sousou-no-frieren", SourceUrlValidation.DeriveSlug(uri, "https://example.org/anime/{slug}/{episode}"));
    }

    [Fact]
    public void DeriveSlug_TemplateWithoutSlug_FallsBackToLastSegment()
    {
        var uri = new Uri("https://www.example.net/watch/21/one-piece?ep=1");

        Assert.Equal("one-piece", SourceUrlValidation.DeriveSlug(uri, "https://www.example.net/watch/{anilist_id}?ep={episode}"));
    }

    [Fact]
    public void SlugAtTemplatePosition_PrefixMismatch_ReturnsNull_NoFallback()
    {
        var uri = new Uri("https://example.org/browse/sousou-no-frieren");

        Assert.Null(SourceUrlValidation.SlugAtTemplatePosition(uri, "https://example.org/anime/{slug}/{episode}"));
    }

    // Some sites wrap the slug in literal text on both their anime and episode pages,
    // e.g. /anime/{slug}-sub-espanol, /ver/{slug}-episodio-{episode}.
    [Theory]
    [InlineData("https://example.com/anime/sousou-no-frieren-sub-espanol", "https://example.com/anime/{slug}-sub-espanol")]
    [InlineData("https://example.com/ver/sousou-no-frieren-episodio-12", "https://example.com/ver/{slug}-episodio-{episode}")]
    public void SlugAtTemplatePosition_SlugWrappedInLiteralText(string url, string template)
    {
        Assert.Equal("sousou-no-frieren", SourceUrlValidation.SlugAtTemplatePosition(new Uri(url), template));
    }

    [Theory]
    // Suffix missing: the 2nd-season page must not be read as a slug ending in "-2nd-season-sub-espanol".
    [InlineData("https://example.com/anime/sousou-no-frieren-latino", "https://example.com/anime/{slug}-sub-espanol")]
    // Nothing left for the slug once the literal is stripped.
    [InlineData("https://example.com/anime/-sub-espanol", "https://example.com/anime/{slug}-sub-espanol")]
    // No literal between two placeholders: ambiguous, never guessed.
    [InlineData("https://example.com/ver/frieren12", "https://example.com/ver/{slug}{episode}")]
    public void SlugAtTemplatePosition_WrappedSlug_NoMatch_ReturnsNull(string url, string template)
    {
        Assert.Null(SourceUrlValidation.SlugAtTemplatePosition(new Uri(url), template));
    }

    [Fact]
    public void SlugAtTemplatePosition_UnrelatedLink_ReturnsNull()
    {
        var uri = new Uri("https://example.org/contact");

        Assert.Null(SourceUrlValidation.SlugAtTemplatePosition(uri, "https://example.org/anime/{slug}/{episode}"));
    }

    [Theory]
    [InlineData("Sousou no Frieren", "sousou-no-frieren")]
    [InlineData("Sousou no Frieren 2nd Season", "sousou-no-frieren-2nd-season")]
    [InlineData("Attack on Titan: Final Season", "attack-on-titan-final-season")]
    [InlineData("  Re:ZERO -Starting Life in Another World-  ", "re-zero-starting-life-in-another-world")]
    public void TitleSlug_MatchesSiteConventions(string title, string expected)
    {
        Assert.Equal(expected, SourceUrlValidation.TitleSlug(title));
    }
}
