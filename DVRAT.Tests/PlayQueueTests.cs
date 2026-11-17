using System;
using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class PlayQueueTests
{
    private static readonly string[] Songs = { "a", "b", "c", "d", "e" };

    private static List<string> PlayToEnd(PlayQueue queue)
    {
        var played = new List<string> { queue.Current };
        while (queue.MoveNext())
            played.Add(queue.Current);
        return played;
    }

    [Fact]
    public void PlaysOnFromTheStartSongInFolderOrder()
    {
        var queue = new PlayQueue(Songs, 2, false, new Random(1));

        Assert.Equal(new[] { "c", "d", "e" }, PlayToEnd(queue));
        Assert.Equal("e", queue.Current);
    }

    [Fact]
    public void PreviousStopsAtTheFirstSong()
    {
        var queue = new PlayQueue(Songs, 1, false, new Random(1));

        Assert.True(queue.MovePrevious());
        Assert.Equal("a", queue.Current);
        Assert.False(queue.MovePrevious());
        Assert.Equal("a", queue.Current);
    }

    [Fact]
    public void ShuffleKeepsTheCurrentSongAndPlaysEveryOtherOnce()
    {
        var queue = new PlayQueue(Songs, 3, true, new Random(7));

        var played = PlayToEnd(queue);

        Assert.Equal("d", played[0]);
        Assert.Equal(Songs.OrderBy(s => s), played.OrderBy(s => s));
    }

    [Fact]
    public void TurningShuffleOffGoesOnAfterTheCurrentSong()
    {
        var queue = new PlayQueue(Songs, 0, true, new Random(3));
        queue.MoveNext();
        string current = queue.Current;

        queue.Shuffle = false;

        Assert.Equal(current, queue.Current);
        Assert.Equal(Songs.SkipWhile(s => s != current), PlayToEnd(queue));
    }

    [Fact]
    public void ASingleSongQueueHasNoNextOrPrevious()
    {
        var queue = new PlayQueue(new[] { "only" }, 0, true, new Random(1));

        Assert.False(queue.MoveNext());
        Assert.False(queue.MovePrevious());
        Assert.Equal("only", queue.Current);
    }
}
