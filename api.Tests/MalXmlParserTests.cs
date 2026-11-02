using System.Text;
using AniTether.Api.Data;
using AniTether.Api.Features.Import;
using Xunit;

namespace AniTether.Api.Tests;

public class MalXmlParserTests
{
    private static Stream ToStream(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void Parse_NormalEntry_MapsAllFields()
    {
        var xml = """
            <myanimelist>
              <anime>
                <series_animedb_id>1</series_animedb_id>
                <my_watched_episodes>12</my_watched_episodes>
                <my_status>Watching</my_status>
                <my_score>8</my_score>
                <my_rewatching>0</my_rewatching>
                <my_start_date>2024-01-15</my_start_date>
                <my_finish_date>0000-00-00</my_finish_date>
              </anime>
            </myanimelist>
            """;

        var items = MalXmlParser.Parse(ToStream(xml));

        var item = Assert.Single(items);
        Assert.Equal(1, item.MalId);
        Assert.Equal(12, item.WatchedEpisodes);
        Assert.Equal(ListStatus.Watching, item.Status);
        Assert.Equal(8, item.Score);
        Assert.Equal(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero), item.StartedAt);
        Assert.Null(item.FinishedAt);
    }

    [Fact]
    public void Parse_RewatchingFlag_OverridesStatus()
    {
        var xml = """
            <myanimelist>
              <anime>
                <series_animedb_id>2</series_animedb_id>
                <my_watched_episodes>24</my_watched_episodes>
                <my_status>Completed</my_status>
                <my_score>0</my_score>
                <my_rewatching>1</my_rewatching>
              </anime>
            </myanimelist>
            """;

        var items = MalXmlParser.Parse(ToStream(xml));

        var item = Assert.Single(items);
        Assert.Equal(ListStatus.Rewatching, item.Status);
        Assert.Null(item.Score);
    }

    [Fact]
    public void Parse_MissingId_SkipsEntry()
    {
        var xml = """
            <myanimelist>
              <anime>
                <my_watched_episodes>5</my_watched_episodes>
                <my_status>Watching</my_status>
              </anime>
              <anime>
                <series_animedb_id>3</series_animedb_id>
                <my_watched_episodes>1</my_watched_episodes>
                <my_status>Plan to Watch</my_status>
              </anime>
            </myanimelist>
            """;

        var items = MalXmlParser.Parse(ToStream(xml));

        var item = Assert.Single(items);
        Assert.Equal(3, item.MalId);
        Assert.Equal(ListStatus.PlanToWatch, item.Status);
    }
}
