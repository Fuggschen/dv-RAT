using System;
using DVRAT;
using Xunit;

public class FalloffTests
{
    private const float Near = 2f, Far = 40f;

    private static double Db(float gain) => 20 * Math.Log10(gain);

    [Fact]
    public void FullUpCloseAndSilentPastTheRange()
    {
        Assert.Equal(1f, Falloff.Gain(1f, Near, Far));
        Assert.Equal(0f, Falloff.Gain(Far, Near, Far));
        Assert.Equal(0f, Falloff.Gain(Far + 10, Near, Far));
    }

    [Fact]
    public void FallsLikeALoudspeakerBeforeTheFade()
    {
        Assert.InRange(Db(Falloff.Gain(2 * Near, Near, Far)), -6.1, -5.9);
        Assert.InRange(Db(Falloff.Gain(4 * Near, Near, Far)), -12.1, -11.9);
    }

    [Fact]
    public void FadesOutWithoutAStep()
    {
        // No audible step at the edge.
        Assert.True(Db(Falloff.Gain(Far - 1, Near, Far)) < -55);
        float previous = 1f;
        for (float d = Near; d < Far; d += 0.25f)
        {
            float gain = Falloff.Gain(d, Near, Far);
            Assert.True(gain <= previous);
            Assert.True(previous - gain < 0.15f);
            previous = gain;
        }
    }
}
