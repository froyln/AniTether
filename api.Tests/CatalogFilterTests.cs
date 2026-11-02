using AniTether.Api.Features.Anime;
using Xunit;

namespace AniTether.Api.Tests;

public class CatalogFilterTests
{
    private static (CatalogFilter? Filter, string? Error) Parse(
        string? q = null, string? genres = null, string? format = null, int? year = null,
        string? season = null, string? status = null, string? sort = null) =>
        CatalogFilter.Parse(q, genres, format, year, season, status, sort);

    [Fact]
    public void NoFilters_DefaultsToTrending()
    {
        var (filter, error) = Parse();

        Assert.Null(error);
        Assert.Equal("TRENDING_DESC", filter!.Sort);
        Assert.Null(filter.Search);
        Assert.Null(filter.Genres);
    }

    [Fact]
    public void SearchText_DefaultsToRelevance()
    {
        var (filter, _) = Parse(q: "  frieren ");

        Assert.Equal("frieren", filter!.Search);
        Assert.Equal("SEARCH_MATCH", filter.Sort);
    }

    [Fact]
    public void RelevanceWithoutSearch_FallsBackToTrending()
    {
        var (filter, _) = Parse(sort: "relevance");

        Assert.Equal("TRENDING_DESC", filter!.Sort);
    }

    [Fact]
    public void AllFilters_ParsedAndMapped()
    {
        var (filter, error) = Parse(genres: "Action, Slice of Life,Action", format: "MOVIE", year: 2024,
            season: "FALL", status: "FINISHED", sort: "score");

        Assert.Null(error);
        Assert.Equal(["Action", "Slice of Life"], filter!.Genres);
        Assert.Equal("MOVIE", filter.Format);
        Assert.Equal(2024, filter.Year);
        Assert.Equal("FALL", filter.Season);
        Assert.Equal("FINISHED", filter.Status);
        Assert.Equal("SCORE_DESC", filter.Sort);
    }

    [Theory]
    [InlineData(null, null, "movie", null, null, null, null)] // enums are case-sensitive, as AniList's are
    [InlineData(null, null, "BLURAY", null, null, null, null)]
    [InlineData(null, null, null, null, "AUTUMN", null, null)]
    [InlineData(null, null, null, null, null, "AIRING", null)]
    [InlineData(null, null, null, null, null, null, "TRENDING_DESC")] // raw AniList sort names aren't accepted
    [InlineData(null, null, null, 1800, null, null, null)]
    [InlineData(null, null, null, 9999, null, null, null)]
    [InlineData(null, "Action\"){id}", null, null, null, null, null)]
    [InlineData(null, "A,B,C,D,E,F", null, null, null, null, null)]
    public void InvalidInput_ReturnsError(string? q, string? genres, string? format, int? year, string? season, string? status, string? sort)
    {
        var (filter, error) = Parse(q, genres, format, year, season, status, sort);

        Assert.Null(filter);
        Assert.NotNull(error);
    }

    [Fact]
    public void OverlongSearch_ReturnsError()
    {
        var (filter, _) = Parse(q: new string('a', 101));

        Assert.Null(filter);
    }
}
