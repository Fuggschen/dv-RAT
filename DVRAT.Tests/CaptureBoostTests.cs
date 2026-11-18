using DVRAT;
using Xunit;

public class CaptureBoostTests
{
    [Fact]
    public void GainFollowsDecibels()
    {
        Assert.Equal(1f, CaptureBoost.Gain(0));
        Assert.Equal(2f, CaptureBoost.Gain(6), 1);
        Assert.Equal(7.94f, CaptureBoost.Gain(18), 2);
    }

    [Fact]
    public void GainStaysWithinTheSliderRange()
    {
        Assert.Equal(1f, CaptureBoost.Gain(-6));
        Assert.Equal(CaptureBoost.Gain(CaptureBoost.MaxDb), CaptureBoost.Gain(CaptureBoost.MaxDb + 12));
    }

    [Fact]
    public void QuietSoundIsOnlyMultiplied()
    {
        var samples = new[] { 0.1f, -0.2f };
        CaptureBoost.Apply(samples, 2, 2f);
        Assert.Equal(0.2f, samples[0], 5);
        Assert.Equal(-0.4f, samples[1], 5);
    }

    [Fact]
    public void PeaksNeverPassFullScaleAndStaySmooth()
    {
        float last = 0f;
        for (float x = 0f; x <= 20f; x += 0.01f)
        {
            float y = SoftKnee.Clip(x);
            Assert.InRange(y, last, 1f);
            Assert.Equal(-y, SoftKnee.Clip(-x));
            last = y;
        }
        Assert.True(SoftKnee.Clip(0.81f) > 0.8f);
    }
}
