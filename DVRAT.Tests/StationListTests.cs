using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class StationListTests
{
    [Fact]
    public void ReadsTheGamesRadioPls()
    {
        const string pls = "[playlist]\r\n\r\nFile1=https://simulatorradio.stream/320\r\nTitle1=Simulator Radio\r\n\r\n" +
                           "File2=http://radio.truckers.fm\r\nTitle2=TruckersFM\r\n\r\nNumberOfEntries=2\r\nVersion=2\r\n";

        var stations = StationList.FromPls(pls);

        Assert.Equal(new[] { "Simulator Radio", "TruckersFM" }, stations.Select(s => s.Name));
        Assert.Equal("http://radio.truckers.fm", stations[1].Url);
    }

    [Fact]
    public void NamesUntitledPlsEntriesByTheirLink()
    {
        var stations = StationList.FromPls("[playlist]\nFile1=https://a.example/live\nFile2=not a link");

        Assert.Equal("https://a.example/live", stations.Single().Name);
    }

    [Fact]
    public void ReadsStationsJsonAndReportsBadEntries()
    {
        var problems = new List<string>();
        var stations = StationList.FromJson(
            "{\"stations\": [{\"name\": \"A\", \"url\": \"https://a.example/live\"}, {\"name\": \"B\"}, {\"url\": \"http://c.example\"}]}",
            problems);

        Assert.Equal(new[] { "A", "http://c.example" }, stations.Select(s => s.Name));
        Assert.Single(problems);
    }

    [Fact]
    public void ReportsBrokenFiles()
    {
        var problems = new List<string>();

        Assert.Empty(StationList.FromJson("{ broken", problems));
        Assert.Empty(StationList.FromJson("[]", problems));
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void WritesWhatItReads()
    {
        var stations = new[] { new RadioStation("A", "https://a.example/live"), new RadioStation("B", "http://b.example") };

        var back = StationList.FromJson(StationList.ToJson(stations), new List<string>());

        Assert.Equal(stations.Select(s => s.Name + s.Url), back.Select(s => s.Name + s.Url));
    }
}
