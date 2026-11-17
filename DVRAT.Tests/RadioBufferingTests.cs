using System;
using DVRAT;
using Xunit;

public class RadioBufferingTests
{
    private const int Rate = 1000;
    private const float OneBar = 0.3f;

    /// <summary>One level per 10 ms, constant 1 at <paramref name="strength"/>.</summary>
    private static float[] Levels(RadioBuffering radio, float strength, int seconds)
    {
        var levels = new float[seconds * 100];
        var block = new float[Rate * 2 / 100];
        for (int i = 0; i < levels.Length; i++)
        {
            for (int s = 0; s < block.Length; s++) block[s] = 1f;
            radio.Process(block, 0, block.Length, strength);
            levels[i] = block[block.Length - 1];
        }
        return levels;
    }

    private static int Silent(float[] levels, int from = 0)
    {
        int silent = 0;
        for (int i = from; i < levels.Length; i++)
            if (levels[i] == 0f) silent++;
        return silent;
    }

    [Fact]
    public void FromTwoBarsUpItNeverStalls()
    {
        foreach (float strength in new[] { SignalLoad.TwoBars, 0.6f, 1f })
        {
            var radio = new RadioBuffering(Rate, 2);
            var levels = Levels(radio, strength, 60);
            Assert.Equal(0, Silent(levels));
            Assert.Equal(1f, levels[levels.Length - 1]);
            Assert.False(radio.Buffering);
        }
    }

    [Fact]
    public void TunedInAtOneBarItFillsTheBufferFirst()
    {
        var radio = new RadioBuffering(Rate, 2);
        var levels = Levels(radio, OneBar, 2);
        Assert.True(radio.Buffering);
        Assert.Equal(levels.Length, Silent(levels));
    }

    [Fact]
    public void AtOneBarItPlaysAndStallsByTurns()
    {
        var radio = new RadioBuffering(Rate, 2);
        Levels(radio, 1f, 10);
        var levels = Levels(radio, OneBar, 120);

        int stalls = 0;
        for (int i = 1; i < levels.Length; i++)
            if (levels[i] == 0f && levels[i - 1] > 0f) stalls++;
        Assert.InRange(stalls, 3, 30);
        Assert.InRange(Silent(levels), levels.Length / 10, levels.Length * 9 / 10);
    }

    [Fact]
    public void WeakerSignalStallsLonger()
    {
        var weak = new RadioBuffering(Rate, 2);
        var better = new RadioBuffering(Rate, 2);
        Assert.True(Silent(Levels(weak, SignalLoad.OneBar, 120)) > Silent(Levels(better, 0.45f, 120)));
    }

    [Fact]
    public void WithNoBarsItBuffersUntilTheSignalReturns()
    {
        var radio = new RadioBuffering(Rate, 2);
        Levels(radio, 1f, 10);
        var lost = Levels(radio, 0.1f, 120);
        Assert.True(radio.Buffering);
        Assert.Equal(0f, lost[lost.Length - 1]);
        // The full buffer plays out first.
        Assert.InRange(Silent(lost), lost.Length - (int)(RadioBuffering.MaxBufferSeconds * 100) - 2, lost.Length);

        var back = Levels(radio, 1f, 5);
        Assert.False(radio.Buffering);
        Assert.Equal(1f, back[back.Length - 1]);
    }

    [Fact]
    public void StallsFadeInsteadOfCutting()
    {
        var radio = new RadioBuffering(Rate, 2);
        Levels(radio, 1f, 10);
        var block = new float[Rate * 2 * 10];
        for (int s = 0; s < block.Length; s++) block[s] = 1f;
        radio.Process(block, 0, block.Length, 0.1f);

        float biggestStep = 0f;
        for (int s = 2; s < block.Length; s += 2)
            biggestStep = Math.Max(biggestStep, Math.Abs(block[s] - block[s - 2]));
        Assert.True(biggestStep < 0.2f, $"step {biggestStep}");
    }

    [Fact]
    public void RateRisesWithTheSignal()
    {
        Assert.Equal(0f, RadioBuffering.Rate(0.2f));
        Assert.Equal(1f, RadioBuffering.Rate(SignalLoad.TwoBars), 3);
        Assert.InRange(RadioBuffering.Rate(SignalLoad.TwoBars - 0.001f), 0.98f, 1f);
        Assert.True(RadioBuffering.Rate(SignalLoad.OneBar) < RadioBuffering.Rate(0.4f));
        Assert.True(RadioBuffering.Rate(0.6f) < RadioBuffering.Rate(1f));
    }
}
