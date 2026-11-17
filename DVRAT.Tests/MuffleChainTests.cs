using System;
using DVRAT;
using Xunit;

public class MuffleChainTests
{
    private const int Rate = 48000;

    private static float[] Sine(double hz, double seconds = 1)
    {
        int frames = (int)(Rate * seconds);
        var data = new float[frames * 2];
        for (int i = 0; i < frames; i++)
            data[2 * i] = data[2 * i + 1] = (float)(0.25 * Math.Sin(2 * Math.PI * hz * i / Rate));
        return data;
    }

    // Left channel RMS over the second half, after fade and filter settle.
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

    private static double GainDb(double hz, bool walled, bool lowPass = true)
    {
        var input = Sine(hz);
        var output = (float[])input.Clone();
        new MuffleChain(Rate, 2, lowPass).Process(output, walled);
        return 20 * Math.Log10(Rms(output) / Rms(input));
    }

    [Fact]
    public void ClearSoundIsUntouched()
    {
        var input = Sine(5000);
        var output = (float[])input.Clone();
        new MuffleChain(Rate, 2, true).Process(output, false);
        Assert.Equal(input, output);
    }

    [Fact]
    public void WallTakesOffHighsMoreThanLows()
    {
        Assert.InRange(GainDb(100, true), MuffleChain.GainDb - 0.5, MuffleChain.GainDb + 0.5);
        Assert.True(GainDb(5000, true) < MuffleChain.GainDb - 20);
    }

    [Fact]
    public void BassOnlySourceOnlyGetsQuieter()
    {
        Assert.InRange(GainDb(5000, true, lowPass: false), MuffleChain.BassGainDb - 0.1, MuffleChain.BassGainDb + 0.1);
    }

    [Fact]
    public void FadesOverFadeSeconds()
    {
        var chain = new MuffleChain(Rate, 2, true);
        chain.Process(new float[(int)(Rate * MuffleChain.FadeSeconds / 2) * 2], true);
        Assert.InRange(chain.Mix, 0.45f, 0.55f);
        chain.Process(new float[Rate * 2], true);
        Assert.Equal(1f, chain.Mix);
        chain.Process(new float[Rate * 2], false);
        Assert.Equal(0f, chain.Mix);
    }
}
