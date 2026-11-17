using DVRAT;
using Xunit;

public class PlayTimeTests
{
    [Theory]
    [InlineData(0f, "0:00")]
    [InlineData(7.9f, "0:07")]
    [InlineData(187f, "3:07")]
    [InlineData(3725f, "1:02:05")]
    [InlineData(-3f, "0:00")]
    public void ShowsMinutesAndSecondsLikeAPlayer(float seconds, string expected)
    {
        Assert.Equal(expected, Format.PlayTime(seconds));
    }
}
