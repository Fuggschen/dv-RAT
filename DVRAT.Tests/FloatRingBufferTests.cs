using DVRAT;
using Xunit;

public class FloatRingBufferTests
{
    [Fact]
    public void ReadsBackWhatWasWritten()
    {
        var ring = new FloatRingBuffer(8);
        ring.Write(new[] { 1f, 2f, 3f }, 0, 3);

        var target = new float[3];
        int read = ring.Read(target, 0, 3);

        Assert.Equal(3, read);
        Assert.Equal(new[] { 1f, 2f, 3f }, target);
        Assert.Equal(0, ring.Count);
    }

    [Fact]
    public void WritesOnlyWhatFits()
    {
        var ring = new FloatRingBuffer(4);

        int written = ring.Write(new[] { 1f, 2f, 3f, 4f, 5f, 6f }, 0, 6);

        Assert.Equal(4, written);
        Assert.Equal(4, ring.Count);
    }

    [Fact]
    public void ReadsOnlyWhatIsThere()
    {
        var ring = new FloatRingBuffer(8);
        ring.Write(new[] { 7f, 8f }, 0, 2);

        var target = new float[5];
        int read = ring.Read(target, 0, 5);

        Assert.Equal(2, read);
        Assert.Equal(new[] { 7f, 8f, 0f, 0f, 0f }, target);
    }

    [Fact]
    public void KeepsOrderAcrossTheWrap()
    {
        var ring = new FloatRingBuffer(4);
        var target = new float[4];
        ring.Write(new[] { 1f, 2f, 3f }, 0, 3);
        ring.Read(target, 0, 2);

        ring.Write(new[] { 4f, 5f, 6f }, 0, 3);
        int read = ring.Read(target, 0, 4);

        Assert.Equal(4, read);
        Assert.Equal(new[] { 3f, 4f, 5f, 6f }, target);
    }

    [Fact]
    public void HonoursOffsets()
    {
        var ring = new FloatRingBuffer(4);
        ring.Write(new[] { 0f, 9f, 8f }, 1, 2);

        var target = new float[3];
        ring.Read(target, 1, 2);

        Assert.Equal(new[] { 0f, 9f, 8f }, target);
    }
}
