using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DVRAT;
using Xunit;

public class FrameReaderTests
{
    private static byte[] Frame(char type, byte[] payload)
    {
        var frame = new byte[5 + payload.Length];
        frame[0] = (byte)type;
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 1);
        payload.CopyTo(frame, 5);
        return frame;
    }

    private static Stream Of(params byte[][] frames)
    {
        var all = new MemoryStream();
        foreach (var frame in frames) all.Write(frame, 0, frame.Length);
        all.Position = 0;
        return all;
    }

    private static byte[] ReadAll(Stream stream, int chunk)
    {
        var result = new MemoryStream();
        var buffer = new byte[chunk];
        int got;
        while ((got = stream.Read(buffer, 0, buffer.Length)) > 0) result.Write(buffer, 0, got);
        return result.ToArray();
    }

    [Fact]
    public void SoundComesOutAndLinesGoToTheCallback()
    {
        var lines = new List<string>();
        var reader = new FrameReader(Of(
            Frame('S', Encoding.UTF8.GetBytes("capturing")),
            Frame('A', new byte[] { 1, 2, 3, 4 }),
            Frame('S', Encoding.UTF8.GetBytes("media\tPlaying\tÄrzte\tSchrei nach Liebe")),
            Frame('A', new byte[] { 5, 6, 7, 8, 9, 10, 11, 12 })), lines.Add);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, ReadAll(reader, 3));
        Assert.Equal(new[] { "capturing", "media\tPlaying\tÄrzte\tSchrei nach Liebe" }, lines);
    }

    [Fact]
    public void LinesOnlyEndWithTheStream()
    {
        var lines = new List<string>();
        var reader = new FrameReader(Of(Frame('S', Encoding.UTF8.GetBytes("app\t1\tspotify\tSpotify\t1"))), lines.Add);
        Assert.Empty(ReadAll(reader, 16));
        Assert.Single(lines);
    }

    [Fact]
    public void ACutOffFrameEndsTheStream()
    {
        var whole = Frame('A', new byte[] { 1, 2, 3, 4 });
        var cut = new byte[7];
        Array.Copy(whole, cut, 7);
        var reader = new FrameReader(Of(cut), _ => { });
        Assert.Equal(new byte[] { 1, 2 }, ReadAll(reader, 16));
    }
}
