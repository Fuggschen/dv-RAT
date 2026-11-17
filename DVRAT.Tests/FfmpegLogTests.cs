using DVRAT;
using Xunit;

public class FfmpegLogTests
{
    [Fact]
    public void ReadsTheDurationLine()
    {
        Assert.Equal(321.29, FfmpegLog.Duration("[info]   Duration: 00:05:21.29, start: 0.000000, bitrate: 131 kb/s").Value, 3);
        Assert.Equal(3725.0, FfmpegLog.Duration("[info]   Duration: 01:02:05.00, start: 0.000000").Value, 3);
    }

    [Fact]
    public void IgnoresOtherLinesAndUnknownLengths()
    {
        Assert.Null(FfmpegLog.Duration("[info]   Duration: N/A, bitrate: N/A"));
        Assert.Null(FfmpegLog.Duration("[info]     comment         : Duration: 00:01:00.00"));
        Assert.Null(FfmpegLog.Duration(null));
    }

    [Fact]
    public void ReadsErrorAndFatalLines()
    {
        Assert.Equal("Error opening input: No such file or directory",
            FfmpegLog.Problem("[in#0 @ 0000024097e76940] [error] Error opening input: No such file or directory"));
        Assert.Equal("Error opening input files: No such file or directory",
            FfmpegLog.Problem("[fatal] Error opening input files: No such file or directory"));
        Assert.Null(FfmpegLog.Problem("[mov,mp4 @ 00000205d0bf7040] [warning] stream 0, timescale not set"));
    }
}
