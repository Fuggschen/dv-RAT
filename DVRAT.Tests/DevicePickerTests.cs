using System.Linq;
using DVRAT;
using Xunit;

public class DevicePickerTests
{
    private static SpeakerKind KindOf(string name) => name.StartsWith("sub") ? SpeakerKind.Subwoofer : SpeakerKind.Speaker;

    [Fact]
    public void TakesFiveSpeakersAndOneSubwooferInOrder()
    {
        var found = new[] { "s1", "sub1", "s2", "s3", "s4", "sub2", "s5", "s6" };

        var picked = DevicePicker.Pick(found, KindOf, d => true);

        Assert.Equal(new[] { "s1", "sub1", "s2", "s3", "s4", "s5" }, picked);
    }

    [Fact]
    public void SkipsDevicesItCannotUse()
    {
        var picked = DevicePicker.Pick(new[] { "s1", "s2", "sub1", "sub2" }, KindOf, d => d != "s1" && d != "sub1");

        Assert.Equal(new[] { "s2", "sub2" }, picked);
    }

    [Fact]
    public void ListsEachDeviceOnce()
    {
        var picked = DevicePicker.Pick(new[] { "s1", "s1", "s2" }, KindOf, d => true);

        Assert.Equal(new[] { "s1", "s2" }, picked);
    }
}

public class AudioFanOutTests
{
    private static float[] Count(int n) => Enumerable.Range(1, n).Select(i => (float)i).ToArray();

    [Fact]
    public void EveryOutputGetsTheSameSamples()
    {
        var fan = new AudioFanOut(8, () => 0);
        var a = fan.AddOutput();
        var b = fan.AddOutput();

        Assert.Equal(4, fan.Write(Count(4), 0, 4));

        var ra = new float[4];
        var rb = new float[4];
        fan.Read(a, ra, 0, 4);
        fan.Read(b, rb, 0, 4);
        Assert.Equal(Count(4), ra);
        Assert.Equal(Count(4), rb);
    }

    [Fact]
    public void ALateOutputStartsInStepWithTheFirst()
    {
        var fan = new AudioFanOut(16, () => 0);
        var first = fan.AddOutput();
        fan.Write(Count(6), 0, 6);

        var late = fan.AddOutput();
        fan.Write(new[] { 99f }, 0, 1);

        // Both hold 7 samples, so the 99 reaches both speakers at once.
        Assert.Equal(first.Count, late.Count);
        var drained = new float[7];
        fan.Read(late, drained, 0, 7);
        Assert.Equal(99f, drained[6]);
    }

    [Fact]
    public void OutputsReadingAtDifferentMomentsLoseNoSamples()
    {
        long now = 0;
        var fan = new AudioFanOut(8, () => now);
        var a = fan.AddOutput();
        var b = fan.AddOutput();
        var source = Count(16);
        int written = fan.Write(source, 0, 16);

        var readA = new float[16];
        var readB = new float[16];
        fan.Read(a, readA, 0, 4);
        written += fan.Write(source, written, 16 - written);
        fan.Read(b, readB, 0, 4);
        written += fan.Write(source, written, 16 - written);
        fan.Read(a, readA, 4, 8);
        fan.Read(b, readB, 4, 8);

        Assert.Equal(Count(12), readA.Take(12));
        Assert.Equal(Count(12), readB.Take(12));
    }

    [Fact]
    public void HoldingFillsTheQueuesWhileNothingReads()
    {
        long now = 0;
        var fan = new AudioFanOut(8, () => now);
        fan.AddOutput();
        fan.Hold();
        now = AudioFanOut.StallMs * 10;

        Assert.Equal(6, fan.Write(Count(6), 0, 6));
        Assert.Equal(6, fan.Queued);
    }

    [Fact]
    public void ReadsWhileHoldingTakeNothing()
    {
        var fan = new AudioFanOut(8, () => 0);
        var output = fan.AddOutput();
        fan.Write(Count(4), 0, 4);
        fan.Hold();

        Assert.Equal(0, fan.Read(output, new float[4], 0, 4));
        Assert.Equal(4, output.Count);
    }

    [Fact]
    public void ReleaseLeavesEveryOutputAtTheSameSound()
    {
        var fan = new AudioFanOut(16, () => 0);
        var a = fan.AddOutput();
        var b = fan.AddOutput();
        fan.Write(Count(8), 0, 8);
        fan.Read(a, new float[2], 0, 2);
        fan.Hold();
        fan.Write(Count(12).Skip(8).ToArray(), 0, 4);

        fan.Release(5);

        var readA = new float[5];
        var readB = new float[5];
        Assert.Equal(5, fan.Read(a, readA, 0, 5));
        Assert.Equal(5, fan.Read(b, readB, 0, 5));
        Assert.Equal(Count(12).Skip(7), readA);
        Assert.Equal(readA, readB);
    }

    [Fact]
    public void AnOutputThatStoppedReadingDoesNotHoldBackTheOthers()
    {
        long now = 0;
        var fan = new AudioFanOut(4, () => now);
        var playing = fan.AddOutput();
        fan.AddOutput();
        fan.Write(Count(4), 0, 4);

        now = AudioFanOut.StallMs + 1;
        fan.Read(playing, new float[4], 0, 4);

        Assert.Equal(4, fan.Write(Count(4), 0, 4));
    }

    [Fact]
    public void WritesNothingWhileEveryOutputIsFull()
    {
        var fan = new AudioFanOut(2, () => 0);
        fan.AddOutput();
        fan.Write(Count(2), 0, 2);

        Assert.Equal(0, fan.Write(Count(2), 0, 2));
    }

    [Fact]
    public void WaitsWhileNoOutputReads()
    {
        // Paused game: nothing reads, and writing past capacity would skip the song.
        long now = 0;
        var fan = new AudioFanOut(4, () => now);
        var output = fan.AddOutput();
        fan.Write(Count(4), 0, 4);
        fan.Read(output, new float[2], 0, 2);

        now = AudioFanOut.StallMs * 10;

        Assert.Equal(0, fan.Write(Count(4), 0, 4));
    }

    [Fact]
    public void ClockCountsWhatTheOutputsPlayed()
    {
        long now = 0;
        var fan = new AudioFanOut(8, () => now);
        var output = fan.AddOutput();
        fan.Write(Count(8), 0, 8);
        fan.Read(output, new float[3], 0, 3);

        Assert.Equal(3, fan.Played);
        Assert.False(fan.Drained);
        fan.Read(output, new float[5], 0, 5);
        Assert.True(fan.Drained);
    }
}
