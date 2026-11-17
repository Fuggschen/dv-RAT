using DVRAT;
using Xunit;

public class CaptureProtocolTests
{
    [Fact]
    public void AppLinesRoundTrip()
    {
        var app = CaptureProtocol.ParseApp(CaptureProtocol.AppLine(new CaptureApp(6388, "Spotify", "Spotify Premium", true)));
        Assert.Equal(6388, app.ProcessId);
        Assert.Equal("Spotify", app.ProcessName);
        Assert.Equal("Spotify Premium", app.DisplayName);
        Assert.True(app.HasMedia);
    }

    [Fact]
    public void TabsAndLineBreaksInNamesBecomeSpaces()
    {
        var media = CaptureProtocol.ParseEvent(CaptureProtocol.MediaLine("Playing", "AC\tDC", "Back\nin Black"));
        Assert.Equal(CaptureEventKind.Media, media.Kind);
        Assert.Equal("AC DC", media.Artist);
        Assert.Equal("Back in Black", media.Title);
        Assert.Equal("Playing", media.Status);
    }

    [Fact]
    public void ReadsEveryEvent()
    {
        Assert.Equal(CaptureEventKind.Waiting, CaptureProtocol.ParseEvent(CaptureProtocol.WaitingLine()).Kind);
        Assert.Equal(CaptureEventKind.Capturing, CaptureProtocol.ParseEvent(CaptureProtocol.CapturingLine()).Kind);
        Assert.Equal(CaptureEventKind.NoMedia, CaptureProtocol.ParseEvent(CaptureProtocol.NoMediaLine()).Kind);
        var error = CaptureProtocol.ParseEvent(CaptureProtocol.ErrorLine("Windows refused"));
        Assert.Equal(CaptureEventKind.Error, error.Kind);
        Assert.Equal("Windows refused", error.Message);
        Assert.Equal("hello", CaptureProtocol.ParseEvent(CaptureProtocol.LogLine("hello")).Message);
    }

    [Fact]
    public void EmptyArtistAndTitleStayEmpty()
    {
        var media = CaptureProtocol.ParseEvent(CaptureProtocol.MediaLine("Paused", null, ""));
        Assert.Equal("", media.Artist);
        Assert.Equal("", media.Title);
    }

    [Fact]
    public void UnknownLinesAreNoEvents()
    {
        Assert.Null(CaptureProtocol.ParseEvent("something new"));
        Assert.Null(CaptureProtocol.ParseEvent(""));
        Assert.Null(CaptureProtocol.ParseApp("app\tnot a number\tx\ty\t1"));
        Assert.Null(CaptureProtocol.ParseApp(CaptureProtocol.WaitingLine()));
    }

    [Fact]
    public void SavedLevelsRoundTripExactly()
    {
        var saved = new SavedLevels("{0.0.0.00000000}.{abc}|\\Device\\Spotify.exe%b{00000000}", new[] { 1f, 0.123456789f });
        var back = CaptureProtocol.ParseLevels(CaptureProtocol.LevelsLine(saved));
        Assert.Equal(saved.SessionIdentifier, back.SessionIdentifier);
        Assert.Equal(saved.Levels, back.Levels);
    }

    [Fact]
    public void BrokenSavedLevelsAreSkipped()
    {
        Assert.Null(CaptureProtocol.ParseLevels("id\tnope"));
        Assert.Null(CaptureProtocol.ParseLevels("id\t2"));
        Assert.Null(CaptureProtocol.ParseLevels("\t1"));
        Assert.Null(CaptureProtocol.ParseLevels("just one part"));
    }
}
