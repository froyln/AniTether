using AniTether.Api.Features.Anime;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AniTether.Api.Tests;

public class AniListGenreImagesTests
{
    // g0 (Action) and g1 (Adventure) share their top show, the real case; g1 must fall
    // through to its next one. The http:// banner is skipped for the https cover under it.
    // g2 is a null alias (partial result), g3 has null media, every other alias is missing —
    // all just leave that genre out.
    private const string Response = """
        {"data": {
          "g0": {"media": [{"bannerImage": "https://img.example/aot.jpg", "coverImage": {"extraLarge": "https://img.example/aot-c.jpg"}}]},
          "g1": {"media": [
            {"bannerImage": "https://img.example/aot.jpg", "coverImage": {"extraLarge": "https://img.example/aot-c.jpg"}},
            {"bannerImage": "http://img.example/op.jpg", "coverImage": {"extraLarge": "https://img.example/op-c.jpg"}}
          ]},
          "g2": null, "g3": {"media": null}
        }}
        """;

    [Fact]
    public async Task Picks_distinct_https_images_in_one_request_then_serves_from_cache()
    {
        var requests = 0;
        string? body = null;
        var handler = new FakeHandler(request =>
        {
            requests++;
            body = request.Content!.ReadAsStringAsync().Result;
            return FakeHandler.Json(Response);
        });
        var client = new AniListClient(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));
        var first = await client.GetGenreImagesAsync();
        var second = await client.GetGenreImagesAsync();

        Assert.Equal(1, requests);
        Assert.Same(first, second);
        Assert.Equal("https://img.example/aot.jpg", first["Action"]);
        Assert.Equal("https://img.example/op-c.jpg", first["Adventure"]);
        Assert.Equal(2, first.Count);
        // Genre names travel as variables, never inside the query text.
        Assert.Contains("\"g14\":\"Slice of Life\"", body);
        Assert.DoesNotContain("Slice of Life", body!.Split("\"variables\"")[0]);
    }

    [Fact]
    public async Task A_failed_call_returns_empty_and_is_not_retried_on_every_view()
    {
        var requests = 0;
        var handler = new FakeHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
        });
        var client = new AniListClient(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()));

        Assert.Empty(await client.GetGenreImagesAsync());
        Assert.Empty(await client.GetGenreImagesAsync());
        Assert.Equal(1, requests);
    }
}
