using AniTether.Api.Data;
using AniTether.Api.Features.Sources;
using Xunit;

namespace AniTether.Api.Tests;

public class SourceLinkResolverTests
{
    private static readonly Source Source = new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Source",
        BaseUrl = "https://example.com",
        SearchUrlTemplate = "https://example.com/search?q={title}",
        EpisodeUrlTemplate = "https://example.com/anime/{slug}/{episode}",
    };

    private static readonly Data.Anime Anime = new() { AnilistId = 1, TitleRomaji = "Sousou no Frieren", TitleEnglish = "Frieren: Beyond Journey's End" };

    private static SourceLinkResolver Resolver(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)));

    [Fact]
    public async Task FindAnimeUrlAsync_MatchesOnRomajiSlug()
    {
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="/anime/sousou-no-frieren/1">Frieren</a>"""));

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Equal("https://example.com/anime/sousou-no-frieren/1", result?.ToString());
    }

    [Fact]
    public async Task FindAnimeUrlAsync_MatchesOnEnglishSlug()
    {
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="/anime/frieren-beyond-journey-s-end/1">Frieren</a>"""));

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Equal("https://example.com/anime/frieren-beyond-journey-s-end/1", result?.ToString());
    }

    [Fact]
    public async Task FindAnimeUrlAsync_WrongHostHref_Ignored()
    {
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="https://evil.example/anime/sousou-no-frieren/1">Frieren</a>"""));

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAnimeUrlAsync_UnrelatedSlug_NoMatch()
    {
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="/anime/some-other-show/1">Other</a>"""));

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAnimeUrlAsync_RedirectResponse_TreatedAsNoMatch()
    {
        var resolver = Resolver(_ => new HttpResponseMessage(System.Net.HttpStatusCode.Found));

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAnimeUrlAsync_NonHtmlContentType_Ignored()
    {
        var resolver = Resolver(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
        });

        var result = await resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAnimeUrlAsync_RequestThrows_IsSourceUnavailable()
    {
        var resolver = Resolver(_ => throw new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<SourceUnavailableException>(() => resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None));
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.ServiceUnavailable)]
    [InlineData(System.Net.HttpStatusCode.TooManyRequests)]
    public async Task FindAnimeUrlAsync_ServerErrorOrRateLimit_IsSourceUnavailable(System.Net.HttpStatusCode status)
    {
        var resolver = Resolver(_ => new HttpResponseMessage(status));

        await Assert.ThrowsAsync<SourceUnavailableException>(() => resolver.FindAnimeUrlAsync(Source, Anime, CancellationToken.None));
    }

    [Fact]
    public async Task FindAnimeUrlAsync_AnimePageTemplate_MatchesDifferentShapeThanEpisodeTemplate()
    {
        // A site whose episode player lives under a completely
        // different path ("/ver/{slug}-{episode}") than the search results, which link
        // to an anime info page ("/anime/{slug}"). Matching against EpisodeUrlTemplate
        // alone could never succeed here — AnimePageUrlTemplate is what the resolver
        // should match against instead.
        var source = new Source
        {
            Id = Source.Id,
            Name = Source.Name,
            BaseUrl = Source.BaseUrl,
            SearchUrlTemplate = Source.SearchUrlTemplate,
            EpisodeUrlTemplate = "https://example.com/ver/{slug}-{episode}",
            AnimePageUrlTemplate = "https://example.com/anime/{slug}",
        };
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="/anime/sousou-no-frieren">Frieren</a>"""));

        var result = await resolver.FindAnimeUrlAsync(source, Anime, CancellationToken.None);

        Assert.Equal("https://example.com/anime/sousou-no-frieren", result?.ToString());
    }

    [Fact]
    public async Task FindAnimeUrlAsync_AnimePageTemplate_EpisodePathHrefStillIgnored()
    {
        // Same source as above: a href under the *episode* path shape should not match,
        // since matching now goes through AnimePageUrlTemplate exclusively.
        var source = new Source
        {
            Id = Source.Id,
            Name = Source.Name,
            BaseUrl = Source.BaseUrl,
            SearchUrlTemplate = Source.SearchUrlTemplate,
            EpisodeUrlTemplate = "https://example.com/ver/{slug}-{episode}",
            AnimePageUrlTemplate = "https://example.com/anime/{slug}",
        };
        var resolver = Resolver(_ => FakeHandler.Html("""<a href="/ver/sousou-no-frieren-1">Frieren</a>"""));

        var result = await resolver.FindAnimeUrlAsync(source, Anime, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAnimeUrlAsync_NoTemplate_ReturnsNullWithoutRequest()
    {
        var called = false;
        var resolver = Resolver(_ =>
        {
            called = true;
            return FakeHandler.Html("<a href=\"/x\"></a>");
        });
        var source = new Source
        {
            Id = Source.Id,
            Name = Source.Name,
            BaseUrl = Source.BaseUrl,
            SearchUrlTemplate = Source.SearchUrlTemplate,
            EpisodeUrlTemplate = null,
        };

        var result = await resolver.FindAnimeUrlAsync(source, Anime, CancellationToken.None);

        Assert.Null(result);
        Assert.False(called);
    }
}
