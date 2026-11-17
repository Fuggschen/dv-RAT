using System.Linq;
using DVRAT;
using Xunit;

public class RadioDirectoryDataTests
{
    [Fact]
    public void ReadsTheServerListOncePerName()
    {
        var servers = RadioDirectoryData.Servers(
            "[{\"ip\":\"1.2.3.4\",\"name\":\"de1.api.radio-browser.info\"},{\"ip\":\"::1\",\"name\":\"de1.api.radio-browser.info\"},{\"name\":\"fi1.api.radio-browser.info\"}]");

        Assert.Equal(new[] { "de1.api.radio-browser.info", "fi1.api.radio-browser.info" }, servers);
    }

    [Fact]
    public void ReadsGenresAndCountries()
    {
        var genres = RadioDirectoryData.Genres("[{\"name\":\"classic rock\",\"stationcount\":900},{\"name\":\"\",\"stationcount\":3}]");
        var countries = RadioDirectoryData.Countries("[{\"name\":\"Germany\",\"iso_3166_1\":\"DE\",\"stationcount\":5592}]");

        Assert.Equal("Classic rock", genres.Single().Name);
        Assert.Equal("classic rock", genres.Single().Id);
        Assert.Equal(("Germany", "DE", 5592), (countries[0].Name, countries[0].Id, countries[0].Stations));
    }

    [Fact]
    public void KeepsOnlyStationsTheTabletCanPlay()
    {
        const string json = "[" +
            "{\"stationuuid\":\"a\",\"name\":\"SWR3\",\"url\":\"https://x/swr3.m3u\",\"url_resolved\":\"https://liveradio.swr.de/swr3/play.mp3\",\"codec\":\"MP3\",\"bitrate\":128,\"hls\":0,\"lastcheckok\":1,\"countrycode\":\"DE\"}," +
            "{\"stationuuid\":\"b\",\"name\":\"HLS one\",\"url_resolved\":\"https://x/live.m3u8\",\"codec\":\"AAC\",\"hls\":1,\"lastcheckok\":1}," +
            "{\"stationuuid\":\"c\",\"name\":\"Video\",\"url_resolved\":\"https://x/v\",\"codec\":\"AAC,H.264\",\"hls\":0,\"lastcheckok\":1}," +
            "{\"stationuuid\":\"d\",\"name\":\"Broken\",\"url_resolved\":\"https://x/b\",\"codec\":\"MP3\",\"hls\":0,\"lastcheckok\":0}," +
            "{\"stationuuid\":\"e\",\"name\":\"AAC plus\",\"url\":\"http://y/aac\",\"url_resolved\":\"\",\"codec\":\"AAC+\",\"hls\":0,\"lastcheckok\":1}" +
            "]";

        var stations = RadioDirectoryData.Stations(json, 10);

        Assert.Equal(new[] { "SWR3", "AAC plus" }, stations.Select(s => s.Name));
        Assert.Equal("https://liveradio.swr.de/swr3/play.mp3", stations[0].Url);
        Assert.Equal("http://y/aac", stations[1].Url);
        Assert.Equal(("a", 128, "DE"), (stations[0].Uuid, stations[0].Bitrate, stations[0].CountryCode));
    }

    [Fact]
    public void StopsAtTheLimitAndSurvivesBrokenAnswers()
    {
        string many = "[" + string.Join(",", Enumerable.Range(1, 5).Select(i =>
            $"{{\"stationuuid\":\"{i}\",\"name\":\"S{i}\",\"url_resolved\":\"https://s/{i}\",\"codec\":\"MP3\",\"hls\":0,\"lastcheckok\":1}}")) + "]";

        Assert.Equal(3, RadioDirectoryData.Stations(many, 3).Count);
        Assert.Empty(RadioDirectoryData.Stations("<html>busy</html>", 3));
        Assert.Empty(RadioDirectoryData.Genres(null));
    }

    [Fact]
    public void BuildsEscapedRequests()
    {
        Assert.Equal("https://de1.api.radio-browser.info/json/stations/search?tag=classic%20rock&tagExact=true&order=clickcount&reverse=true&hidebroken=true&limit=40",
            RadioDirectoryData.StationsByGenreUrl("de1.api.radio-browser.info", "classic rock", 20));
        Assert.Equal("https://s/json/url/a%2Fb", RadioDirectoryData.ClickUrl("s", "a/b"));
    }
}
