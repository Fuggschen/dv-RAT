using System;
using System.IO;
using System.Linq;
using DVRAT;
using Xunit;

public class MusicLibraryTests : IDisposable
{
    private static readonly string[] Playable = { ".mp3", ".ogg", ".wav" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dvrat-music-" + Guid.NewGuid().ToString("N"));

    public MusicLibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Touch(string relativePath)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void FindsPlayableFilesInNestedFolders()
    {
        string top = Touch("a.mp3");
        string nested = Touch(Path.Combine("Artist", "Album", "b.ogg"));
        string wav = Touch(Path.Combine("Artist", "c.wav"));

        var files = MusicLibrary.Scan(_root, Playable);

        Assert.Equal(new[] { top, wav, nested }.OrderBy(f => f), files.OrderBy(f => f));
    }

    [Fact]
    public void IgnoresOtherFiles()
    {
        Touch("cover.jpg");
        Touch("notes.txt");
        Touch("song.m4a");

        Assert.Empty(MusicLibrary.Scan(_root, Playable));
    }

    [Fact]
    public void MatchesExtensionsInAnyCase()
    {
        string upper = Touch("LOUD.MP3");

        Assert.Equal(new[] { upper }, MusicLibrary.Scan(_root, Playable));
    }

    [Fact]
    public void MissingFolderGivesEmptyList()
    {
        Assert.Empty(MusicLibrary.Scan(Path.Combine(_root, "missing"), Playable));
        Assert.Empty(MusicLibrary.Scan("", Playable));
    }

    [Fact]
    public void StopsBelowMaxDepth()
    {
        string path = "";
        for (int i = 0; i <= MusicLibrary.MaxDepth; i++)
            path = Path.Combine(path, "d");
        string deepest = Touch(Path.Combine(path, "too-deep.mp3"));
        string allowed = Touch(Path.Combine(Path.GetDirectoryName(path), "deep.mp3"));

        var files = MusicLibrary.Scan(_root, Playable);

        Assert.Contains(allowed, files);
        Assert.DoesNotContain(deepest, files);
    }
}
