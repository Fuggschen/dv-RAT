using System;
using DVRAT;
using Xunit;

public class ToneChainTests
{
    private const int Rate = 48000;

    /// <summary>One second of identical stereo sine.</summary>
    private static float[] Sine(double hz, float amplitude = 0.25f)
    {
        var data = new float[Rate * 2];
        for (int i = 0; i < Rate; i++)
            data[2 * i] = data[2 * i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate));
        return data;
    }

    /// <summary>Left channel RMS over the second half, after the filters settle.</summary>
    private static double Rms(float[] data)
    {
        double sum = 0;
        int count = 0;
        for (int i = data.Length / 2; i < data.Length; i += 2)
        {
            sum += data[i] * data[i];
            count++;
        }
        return Math.Sqrt(sum / count);
    }

    private static double GainDb(ToneSettings settings, double hz, float amplitude = 0.05f)
    {
        var input = Sine(hz, amplitude);
        var output = (float[])input.Clone();
        new ToneChain(Rate, 2, settings).Process(output);
        return 20 * Math.Log10(Rms(output) / Rms(input));
    }

    [Fact]
    public void FlatSettingsLeaveQuietSoundAlone()
    {
        Assert.InRange(GainDb(new ToneSettings(), 1000), -0.1, 0.1);
        Assert.InRange(GainDb(new ToneSettings(), 60), -0.1, 0.1);
    }

    [Fact]
    public void TabletSpeakerCutsBassAndTreble()
    {
        var tablet = ToneSettings.TabletSpeaker();

        Assert.True(GainDb(tablet, 60) < -15);
        Assert.True(GainDb(tablet, 15000) < -15);
        Assert.InRange(GainDb(tablet, 1000), -1.5, 0.5);
    }

    [Fact]
    public void LowsHighsAndMidsMoveTheirBand()
    {
        Assert.InRange(GainDb(new ToneSettings { LowsDb = 6 }, 50), 5, 7);
        Assert.InRange(GainDb(new ToneSettings { HighsDb = -6 }, 15000), -7, -5);
        Assert.InRange(GainDb(new ToneSettings { MidsDb = 6 }, ToneChain.MidsHz), 5, 7);
        Assert.InRange(GainDb(new ToneSettings { MidsDb = 6 }, 50), -0.5, 1);
    }

    [Fact]
    public void LoudSoundPassesCleanUpToTheKnee()
    {
        // Near full-scale song at full volume.
        var input = Sine(1000, SoftKnee.Knee);
        var output = (float[])input.Clone();
        new ToneChain(Rate, 2, new ToneSettings()).Process(output);
        for (int i = Rate; i < output.Length; i++)
            Assert.InRange(output[i] - input[i], -0.01f, 0.01f);
    }

    [Fact]
    public void BoostsOverdriveWithoutGoingPastFullScale()
    {
        var data = Sine(50, 0.9f);
        new ToneChain(Rate, 2, new ToneSettings { LowsDb = ToneSettings.MaxEqDb }).Process(data);

        foreach (float sample in data)
            Assert.InRange(sample, -1f, 1f);
    }

    [Fact]
    public void KeepsLeftAndRightApart()
    {
        // The filter runs after Unity's 3D panning, so a left-panned sound must stay left.
        var data = new float[Rate * 2];
        for (int i = 0; i < Rate; i++)
            data[2 * i] = (float)(0.2 * Math.Sin(2 * Math.PI * 440 * i / Rate));

        new ToneChain(Rate, 2, ToneSettings.TabletSpeaker()).Process(data);

        for (int i = 1; i < data.Length; i += 2)
            Assert.Equal(0f, data[i]);
    }

    [Fact]
    public void FollowsSettingsChangedBetweenBlocks()
    {
        var settings = new ToneSettings();
        var chain = new ToneChain(Rate, 2, settings);
        chain.Process(Sine(15000, 0.05f));

        settings.HighCutHz = 3000;
        var output = Sine(15000, 0.05f);
        chain.Process(output);

        Assert.True(20 * Math.Log10(Rms(output) / Rms(Sine(15000, 0.05f))) < -15);
    }
}
