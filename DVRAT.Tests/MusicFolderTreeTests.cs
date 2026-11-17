using System.IO;
using DVRAT;
using Xunit;

public class MusicFolderTreeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Music");

    private static string At(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

    [Fact]
    public void ListsFilesDirectlyInEachFolder()
    {
        var tree = new MusicFolderTree(Root, new[] { At("intro.mp3"), At("Artist", "Album", "a.m4a"), At("Artist", "b.mp3") });

        Assert.Equal(new[] { At("intro.mp3") }, tree.Files(Root));
        Assert.Equal(new[] { At("Artist", "b.mp3") }, tree.Files(At("Artist")));
        Assert.Equal(new[] { At("Artist", "Album", "a.m4a") }, tree.Files(At("Artist", "Album")));
    }

    [Fact]
    public void ListsFoldersThatHoldMusicBelowThem()
    {
        var tree = new MusicFolderTree(Root, new[] { At("Artist", "Album", "a.m4a"), At("Other", "c.ogg") });

        Assert.Equal(new[] { At("Artist"), At("Other") }, tree.Folders(Root));
        Assert.Equal(new[] { At("Artist", "Album") }, tree.Folders(At("Artist")));
        Assert.Empty(tree.Files(At("Artist")));
    }

    [Fact]
    public void ListsEachFolderOnceInScanOrder()
    {
        var tree = new MusicFolderTree(Root, new[] { At("B", "1.mp3"), At("A", "1.mp3"), At("B", "2.mp3") });

        Assert.Equal(new[] { At("B"), At("A") }, tree.Folders(Root));
    }

    [Fact]
    public void IgnoresFilesOutsideTheRootAndTrailingSeparators()
    {
        var tree = new MusicFolderTree(Root + Path.DirectorySeparatorChar,
            new[] { At("a.mp3"), Path.Combine(Path.GetTempPath(), "MusicOld", "x.mp3") });

        Assert.Equal(new[] { At("a.mp3") }, tree.Files(Root));
        Assert.Empty(tree.Folders(Root));
    }

    [Fact]
    public void WorksWithADriveRootAsMusicFolder()
    {
        string drive = Path.GetPathRoot(System.Environment.CurrentDirectory);
        string top = drive + "a.mp3";
        string nested = Path.Combine(drive, "X", "b.mp3");

        var tree = new MusicFolderTree(drive, new[] { top, nested });

        Assert.Equal(new[] { top }, tree.Files(tree.Root));
        Assert.Equal(new[] { Path.Combine(drive, "X") }, tree.Folders(tree.Root));
        Assert.Equal(tree.Root, tree.Parent(Path.Combine(drive, "X")));
    }

    [Fact]
    public void WalkingUpFromAFolderOutsideTheRootEnds()
    {
        var tree = new MusicFolderTree(Root, new[] { At("a.mp3") });
        string elsewhere = Path.Combine(Path.GetPathRoot(Root), "Elsewhere", "Deep");

        int steps = 0;
        for (string folder = elsewhere; folder != null && steps < 50; folder = tree.Parent(folder))
            steps++;

        Assert.True(steps < 50);
        Assert.False(tree.Contains(elsewhere));
        Assert.True(tree.Contains(At("Artist")));
    }

    [Fact]
    public void ParentEndsAtTheRoot()
    {
        var tree = new MusicFolderTree(Root, new[] { At("Artist", "Album", "a.mp3") });

        Assert.Equal(At("Artist"), tree.Parent(At("Artist", "Album")));
        Assert.Equal(Root, tree.Parent(At("Artist")));
        Assert.Null(tree.Parent(Root));
    }
}
