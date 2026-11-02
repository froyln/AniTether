using System.Net;
using AniTether.Api.Features.Anime;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace AniTether.Api.Tests;

public class AniListClientTests
{
    private static AniListClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)), new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetById_NotFound_IsNull()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetByIdAsync(99999999));
    }

    [Fact]
    public async Task GetById_RateLimited_Throws()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetByIdAsync(1));
    }

    [Fact]
    public async Task GetUserList_FlattensEveryList()
    {
        var client = Client(_ => FakeHandler.Json("""
            {"data": {"MediaListCollection": {"lists": [
              {"entries": [{"status": "CURRENT", "progress": 3, "media": {"id": 1, "title": {"romaji": "A"}}}]},
              {"entries": [{"status": "COMPLETED", "progress": 12, "media": {"id": 2, "title": {"romaji": "B"}}}]}
            ]}}}
            """));

        var entries = await client.GetUserListAsync("someone");

        Assert.Equal([1, 2], entries!.Select(e => e.Media.Id));
    }

    [Fact]
    public async Task Search_ReadsThePageMedia()
    {
        var client = Client(_ => FakeHandler.Json("""{"data": {"Page": {"media": [{"id": 5, "title": {"romaji": "Five"}}]}}}"""));

        Assert.Equal("Five", Assert.Single(await client.SearchAsync("five")).Title.Romaji);
    }
}
