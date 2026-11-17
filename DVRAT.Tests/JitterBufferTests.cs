using System.Collections.Generic;
using DVRAT;
using Xunit;

public class JitterBufferTests
{
    private static byte[] P(uint seq) => new[] { (byte)seq };

    // The packets' first bytes until the buffer is empty, -1 for a gap.
    private static List<int> TakeAll(JitterBuffer buffer)
    {
        var taken = new List<int>();
        while (buffer.TryTake(out byte[] packet))
            taken.Add(packet != null ? packet[0] : -1);
        return taken;
    }

    [Fact]
    public void WaitsForAHeadStartThenPlaysInOrder()
    {
        var buffer = new JitterBuffer();
        foreach (uint seq in new uint[] { 1, 0, 3, 2 })
            buffer.Push(1, seq, P(seq));
        Assert.False(buffer.TryTake(out _));

        buffer.Push(1, 4, P(4));
        Assert.Equal(new List<int> { 0, 1, 2, 3, 4 }, TakeAll(buffer));
    }

    [Fact]
    public void AMissingPacketIsAGapAndALateOneIsDropped()
    {
        var buffer = new JitterBuffer();
        foreach (uint seq in new uint[] { 0, 1, 3, 4, 5 })
            buffer.Push(1, seq, P(seq));

        Assert.Equal(new List<int> { 0, 1, -1, 3, 4, 5 }, TakeAll(buffer));
        buffer.Push(1, 2, P(2));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void TooMuchWaitingDropsTheOldest()
    {
        var buffer = new JitterBuffer();
        for (uint seq = 0; seq < JitterBuffer.MaxPackets + 4; seq++)
            buffer.Push(1, seq, P(seq));

        Assert.Equal(JitterBuffer.MaxPackets, buffer.Count);
        Assert.True(buffer.TryTake(out byte[] first));
        Assert.Equal(4, first[0]);
    }

    [Fact]
    public void ANewEpochStartsOverAndAnOldOneIsIgnored()
    {
        var buffer = new JitterBuffer();
        for (uint seq = 0; seq < 6; seq++)
            buffer.Push(1, seq, P(seq));
        buffer.TryTake(out _);

        buffer.Push(2, 0, P(100));
        buffer.Push(1, 6, P(6));

        Assert.Equal(2u, buffer.Epoch);
        Assert.Equal(1, buffer.Count);
        Assert.False(buffer.TryTake(out _));
    }

    [Fact]
    public void RunningDryWaitsForANewHeadStart()
    {
        var buffer = new JitterBuffer();
        for (uint seq = 0; seq < 5; seq++)
            buffer.Push(1, seq, P(seq));
        TakeAll(buffer);

        buffer.Push(1, 5, P(5));
        Assert.False(buffer.TryTake(out _));
        for (uint seq = 6; seq < 10; seq++)
            buffer.Push(1, seq, P(seq));
        Assert.Equal(new List<int> { 5, 6, 7, 8, 9 }, TakeAll(buffer));
    }
}
