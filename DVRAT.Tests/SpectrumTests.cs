using System;
using DVRAT;
using Xunit;

public class SpectrumTests
{
    private const int Rate = 48000;
    private const int Bands = 16;

    private static float[] Tone(double hz, float amplitude)
    {
        var samples = new float[SpectrumAnalyzer.Size];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
        return samples;
    }

    private static float[] Levels(float[] samples)
    {
        var levels = new float[Bands];
        new SpectrumAnalyzer(Rate, Bands).Analyze(samples, levels);
        return levels;
    }

    [Fact]
    public void AFullScaleToneFillsItsBandAlone()
    {
        var levels = Levels(Tone(1000, 1f));
        int loudest = Array.IndexOf(levels, Max(levels));
        Assert.InRange(levels[loudest], 0.95f, 1f);
        // Far from the tone the bands stay low.
        Assert.True(levels[0] < 0.3f && levels[Bands - 1] < 0.3f);
    }

    [Fact]
    public void ATone20DbDownShowsAThirdLower()
    {
        float full = Max(Levels(Tone(1000, 1f)));
        float quiet = Max(Levels(Tone(1000, 0.1f)));
        Assert.InRange(full - quiet, 0.3f, 0.37f);
    }

    [Fact]
    public void SilenceShowsNothing()
    {
        Assert.All(Levels(new float[SpectrumAnalyzer.Size]), level => Assert.Equal(0f, level));
    }

    [Fact]
    public void HistoryGivesTheSamplesBeforeAPositionAndMixesToMono()
    {
        var history = new SampleHistory(8);
        history.Write(new float[] { 1, 3, 2, 4, 3, 5, 4, 6 }, 8, 2);
        var window = new float[2];
        Assert.True(history.Read(3, window));
        Assert.Equal(new[] { 3f, 4f }, window);
        // Not played yet, and long gone.
        Assert.False(history.Read(5, window));
        history.Write(new float[20], 20, 2);
        Assert.False(history.Read(3, window));
    }

    [Fact]
    public void BarsJumpUpAndFallSlowly()
    {
        var motion = new BarMotion(1);
        motion.Step(new[] { 0.8f }, 0.1f);
        Assert.Equal(0.8f, motion.Levels[0]);
        motion.Step(new[] { 0f }, 0.1f);
        Assert.Equal(0.8f - BarMotion.FallPerSecond * 0.1f, motion.Levels[0], 3);
    }

    private static float Max(float[] values)
    {
        float max = 0f;
        foreach (float value in values) max = Math.Max(max, value);
        return max;
    }
}
