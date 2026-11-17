using DVRAT;
using Xunit;

public class SignalLoadTests
{
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(0.75f, 3f)]
    [InlineData(0.5f, 5f)]
    [InlineData(0.25f, 10f)]
    public void BarThresholdsHitTheirTimes(float strength, float seconds)
    {
        Assert.Equal(seconds, SignalLoad.Duration(strength), 3);
    }

    [Theory]
    [InlineData(0.875f, 2f)]
    [InlineData(0.625f, 4f)]
    [InlineData(0.375f, 7.5f)]
    public void TimeBlendsSmoothlyWithinABar(float strength, float seconds)
    {
        Assert.Equal(seconds, SignalLoad.Duration(strength), 3);
    }

    [Fact]
    public void WeakerSignalNeverLoadsFaster()
    {
        float previous = 0f;
        for (float strength = 1f; strength >= SignalLoad.OneBar; strength -= 0.01f)
        {
            float duration = SignalLoad.Duration(strength);
            Assert.True(duration >= previous, $"{strength}: {duration} < {previous}");
            previous = duration;
        }
    }

    [Fact]
    public void StrengthAboveFullCountsAsFull()
    {
        Assert.Equal(1f, SignalLoad.Duration(1.5f), 3);
    }

    [Fact]
    public void NoBarsStallTheLoadUntilSignalReturns()
    {
        Assert.True(float.IsPositiveInfinity(SignalLoad.Duration(0.24f)));
        Assert.Equal(0.4f, SignalLoad.Advance(0.4f, 30f, 0.1f));

        float progress = SignalLoad.Advance(0.4f, 5f, SignalLoad.OneBar);
        Assert.Equal(0.9f, progress, 3);
    }

    [Fact]
    public void BaseTimeAddsToTheSignalTime()
    {
        Assert.Equal(6f, SignalLoad.Duration(1f, 5f), 3);
        Assert.Equal(15f, SignalLoad.Duration(SignalLoad.OneBar, 5f), 3);
        Assert.Equal(0.5f, SignalLoad.Advance(0f, 3f, 1f, 5f), 3);
    }

    [Fact]
    public void NoBarsStallEvenWithBaseTime()
    {
        Assert.Equal(0.3f, SignalLoad.Advance(0.3f, 60f, 0f, 5f));
    }

    [Fact]
    public void ProgressFollowsTheSignalAsItChanges()
    {
        float progress = SignalLoad.Advance(0f, 5f, SignalLoad.OneBar);
        Assert.Equal(0.5f, progress, 3);

        progress = SignalLoad.Advance(progress, 1f, 1f);
        Assert.Equal(1f, progress, 3);
    }

    [Fact]
    public void ProgressStopsAtOne()
    {
        Assert.Equal(1f, SignalLoad.Advance(0.9f, 5f, 1f));
    }
}
