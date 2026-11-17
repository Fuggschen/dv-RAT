using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DVRAT;
using Xunit;

public class IcyStreamTests
{
    /// <summary>A stream with metaint 4: audio, a length byte and the padded title text, then more audio.</summary>
    private static byte[] Stream(string title)
    {
        var bytes = new MemoryStream();
        bytes.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
        var text = Encoding.UTF8.GetBytes($"StreamTitle='{title}';");
        int blocks = (text.Length + 15) / 16;
        bytes.WriteByte((byte)blocks);
        bytes.Write(text, 0, text.Length);
        bytes.Write(new byte[blocks * 16 - text.Length], 0, blocks * 16 - text.Length);
        bytes.Write(new byte[] { 5, 6, 7, 8 }, 0, 4);
        bytes.WriteByte(0);
        bytes.Write(new byte[] { 9, 10 }, 0, 2);
        return bytes.ToArray();
    }

    private static (List<byte> audio, List<string> titles) Run(byte[] stream, int metaInt, int chunk)
    {
        var audio = new List<byte>();
        var titles = new List<string>();
        var icy = new IcyStream(metaInt, (b, o, c) => audio.AddRange(b.Skip(o).Take(c)), titles.Add);
        for (int i = 0; i < stream.Length; i += chunk)
        {
            var part = stream.Skip(i).Take(chunk).ToArray();
            icy.Push(part, part.Length);
        }
        return (audio, titles);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void SplitsAudioAndTitlesWhereverTheChunksEnd(int chunk)
    {
        var (audio, titles) = Run(Stream("Artist - It's a Song"), 4, chunk);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, audio);
        Assert.Equal(new[] { "Artist - It's a Song" }, titles);
    }

    [Fact]
    public void PassesEverythingOnWithoutMetaInt()
    {
        var (audio, titles) = Run(new byte[] { 1, 2, 3 }, 0, 2);

        Assert.Equal(new byte[] { 1, 2, 3 }, audio);
        Assert.Empty(titles);
    }

    [Fact]
    public void ReadsTitlesWithMoreFieldsAndLatin1()
    {
        Assert.Equal("A - B", IcyStream.StreamTitle(Encoding.UTF8.GetBytes("StreamTitle='A - B';StreamUrl='x';\0\0")));
        Assert.Equal("Café", IcyStream.StreamTitle(Encoding.GetEncoding("ISO-8859-1").GetBytes("StreamTitle='Café';")));
        Assert.Null(IcyStream.StreamTitle(Encoding.UTF8.GetBytes("StreamUrl='x';")));
    }
}

public class RadioPlaylistTests
{
    [Fact]
    public void FindsTheStreamInPlsAndM3u()
    {
        Assert.Equal("https://a.example/live",
            RadioPlaylist.FirstStream("[playlist]\r\nFile1=https://a.example/live\r\nTitle1=A\r\nNumberOfEntries=1"));
        Assert.Equal("http://b.example:8000/stream",
            RadioPlaylist.FirstStream("#EXTM3U\n#EXTINF:-1,B\nhttp://b.example:8000/stream\n"));
    }

    [Fact]
    public void RejectsHlsAndEmptyPlaylists()
    {
        Assert.Null(RadioPlaylist.FirstStream("#EXTM3U\n#EXT-X-VERSION:3\nhttps://c.example/seg1.aac"));
        Assert.Null(RadioPlaylist.FirstStream("[playlist]\nNumberOfEntries=0"));
    }

    [Fact]
    public void TellsPlaylistLinksFromStreams()
    {
        Assert.True(RadioPlaylist.IsPlaylistUrl("https://a.example/listen.pls?x=1"));
        Assert.True(RadioPlaylist.IsPlaylistUrl("http://a.example/radio.M3U"));
        Assert.False(RadioPlaylist.IsPlaylistUrl("https://simulatorradio.stream/320"));
    }

    [Fact]
    public void PicksTheFfmpegFormatFromTheContentType()
    {
        Assert.Equal("mp3", RadioPlaylist.FfmpegFormat("audio/mpeg"));
        Assert.Equal("aac", RadioPlaylist.FfmpegFormat("audio/aacp"));
        Assert.Equal("ogg", RadioPlaylist.FfmpegFormat("application/ogg"));
        Assert.Null(RadioPlaylist.FfmpegFormat(null));
    }
}
