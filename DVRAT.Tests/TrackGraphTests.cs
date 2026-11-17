using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DVRAT;
using Xunit;

public class TrackGraphTests
{
    // Layout used by most tests:
    //
    //   A ==> J ==> B (branch 0)
    //          \==> C (branch 1)
    //
    // All tracks point away from the junction J, so moving forward on A faces J.
    private static TrackGraph Wye(out int a, out int b, out int c)
    {
        var graph = new TrackGraph();
        a = graph.AddTrack("A", 100, 0);
        b = graph.AddTrack("B", 200, 0);
        c = graph.AddTrack("C", 50, 0);
        graph.Connect(a, true, b, true, junction: 0, branch: 0);
        graph.Connect(a, true, c, true, junction: 0, branch: 1);
        // Trailing moves back onto A need no switch setting.
        graph.Connect(b, false, a, false);
        graph.Connect(c, false, a, false);
        return graph;
    }

    private static List<TrackGraph.Step> Find(TrackGraph graph, int startTrack, bool forward, params int[] destinations) =>
        graph.FindPath(new[] { TrackGraph.Node(startTrack, forward) }, new HashSet<int>(destinations), 1000, CancellationToken.None);

    [Fact]
    public void FacingMoveReportsRequiredBranch()
    {
        var graph = Wye(out int a, out _, out int c);

        var path = Find(graph, a, true, c);

        Assert.Equal(new[] { a, c }, path.Select(s => s.Track));
        Assert.Equal(0, path[1].Junction);
        Assert.Equal(1, path[1].Branch);
    }

    [Fact]
    public void TrailingMoveNeedsNoSwitch()
    {
        var graph = Wye(out int a, out int b, out _);

        var path = Find(graph, b, false, a);

        Assert.Equal(new[] { b, a }, path.Select(s => s.Track));
        Assert.Equal(TrackGraph.NoJunction, path[1].Junction);
    }

    [Fact]
    public void ReversesWhenFacingTheWrongWay()
    {
        var graph = Wye(out int a, out _, out int c);

        var path = Find(graph, a, false, c);

        Assert.NotNull(path);
        Assert.Equal(new[] { a, c }, path.Select(s => s.Track));
        Assert.True(path[1].Reverse);
    }

    [Fact]
    public void SwitchingFromDivergingToStraightRequiresReversal()
    {
        // B to C means backing out onto A and then going forward through J.
        var graph = Wye(out int a, out int b, out int c);

        var path = Find(graph, b, false, c);

        Assert.Equal(new[] { b, a, c }, path.Select(s => s.Track));
        Assert.True(path[2].Reverse);
        Assert.Equal(1, path[2].Branch);
    }

    [Fact]
    public void PrefersLongerRouteOverReversalWhenCheaper()
    {
        var graph = new TrackGraph();
        int start = graph.AddTrack("start", 10, 0);
        int loop = graph.AddTrack("loop", 500, 0);
        int target = graph.AddTrack("target", 10, 0);
        graph.Connect(start, true, loop, true);
        graph.Connect(loop, true, target, true);
        graph.Connect(start, false, target, true);

        // Facing forward: 10 + 500 through the loop beats 1000 for reversing.
        var path = Find(graph, start, true, target);

        Assert.Equal(new[] { start, loop, target }, path.Select(s => s.Track));
        Assert.DoesNotContain(path, s => s.Reverse);
    }

    [Fact]
    public void ReturnsNullWhenUnreachable()
    {
        var graph = new TrackGraph();
        int a = graph.AddTrack("A", 10, 0);
        int b = graph.AddTrack("B", 10, 0);

        Assert.Null(Find(graph, a, true, b));
    }

    [Fact]
    public void StartOnDestinationIsOneStep()
    {
        var graph = Wye(out int a, out _, out _);

        var path = Find(graph, a, true, a);

        Assert.Single(path);
    }

    [Fact]
    public void PassesMainLineInsteadOfShorterYardTrack()
    {
        var graph = new TrackGraph();
        int start = graph.AddTrack("start", 10, 0);
        int yard = graph.AddTrack("HB-C3L", 50, 0, yard: true);
        int main = graph.AddTrack("main", 300, 0);
        int target = graph.AddTrack("target", 10, 0);
        graph.Connect(start, true, yard, true, 0, 0);
        graph.Connect(start, true, main, true, 0, 1);
        graph.Connect(yard, true, target, true);
        graph.Connect(main, true, target, true);

        var path = Find(graph, start, true, target);

        Assert.Equal(new[] { start, main, target }, path.Select(s => s.Track));
    }

    [Fact]
    public void EndsOnYardTrackWhenItIsTheDestination()
    {
        var graph = new TrackGraph();
        int start = graph.AddTrack("start", 10, 0);
        int yard = graph.AddTrack("HB-C3L", 50, 0, yard: true);
        graph.Connect(start, true, yard, true);

        var path = Find(graph, start, true, yard);

        Assert.Equal(new[] { start, yard }, path.Select(s => s.Track));
    }

    [Fact]
    public void CrossesTheYardTracksOfTheStationItLeaves()
    {
        // Arrived at the station's entry track, the way on goes through the station's own yard track.
        var graph = new TrackGraph();
        int entry = graph.AddTrack("CS-A1LP", 100, 0, yard: true);
        int through = graph.AddTrack("CS-A2O", 200, 0, yard: true);
        int line = graph.AddTrack("line", 3000, 0);
        int next = graph.AddTrack("SL", 50, 0);
        graph.Connect(entry, true, through, true);
        graph.Connect(through, true, line, true);
        graph.Connect(line, true, next, true);
        var start = new[] { TrackGraph.Node(entry, true) };

        Assert.Null(graph.FindPath(start, new[] { next }, float.PositiveInfinity, CancellationToken.None));
        var path = graph.FindPath(start, new[] { next }, float.PositiveInfinity, CancellationToken.None, new[] { entry, through });
        Assert.Equal(new[] { entry, through, line, next }, path.Select(s => s.Track));
    }

    [Fact]
    public void RouteKeepsToAllowedTracks()
    {
        // A short line without wire and a longer one under it, from A to B.
        var graph = new TrackGraph();
        int a = graph.AddTrack("A", 100, 0);
        int unwired = graph.AddTrack("short", 500, 0);
        int wired = graph.AddTrack("long", 900, 0);
        int b = graph.AddTrack("B", 100, 0);
        graph.Connect(a, true, unwired, true, junction: 0, branch: 0);
        graph.Connect(a, true, wired, true, junction: 0, branch: 1);
        graph.Connect(unwired, true, b, true);
        graph.Connect(wired, true, b, true);
        var start = new[] { TrackGraph.Node(a, true) };
        var stops = new List<ICollection<int>> { new[] { b } };

        var any = graph.FindRoute(start, stops, null, 0f, CancellationToken.None);
        var underWire = graph.FindRoute(start, stops, null, 0f, CancellationToken.None, new[] { a, wired, b });
        var none = graph.FindRoute(start, stops, null, 0f, CancellationToken.None, new[] { a, b });

        Assert.Equal(new[] { a, unwired, b }, any[0].Select(s => s.Track));
        Assert.Equal(new[] { a, wired, b }, underWire[0].Select(s => s.Track));
        Assert.Null(none);
    }

    [Fact]
    public void BridgesShortGapsBetweenAllowedTracks()
    {
        // A - gap - B is wired at both ends of the gap; the spur and the long track join wire only at one end.
        var graph = new TrackGraph();
        int a = graph.AddTrack("A", 1000, 0);
        int gap = graph.AddTrack("gap", 48, 0);
        int b = graph.AddTrack("B", 1000, 0);
        int spur = graph.AddTrack("spur", 30, 0);
        int far = graph.AddTrack("far", 500, 0);
        graph.Connect(a, true, gap, true);
        graph.Connect(gap, false, a, false);
        graph.Connect(gap, true, b, true);
        graph.Connect(b, false, gap, false);
        graph.Connect(b, true, spur, true);
        graph.Connect(spur, false, b, false);
        graph.Connect(a, false, far, true);
        graph.Connect(far, false, a, true);
        graph.Connect(far, true, far, true);

        var bridged = graph.BridgeGaps(new[] { a, b }, 60f);

        Assert.Equal(new[] { a, gap, b }, bridged.OrderBy(t => t));
    }

    private static List<List<TrackGraph.Step>> Route(TrackGraph graph, int start, params int[][] stops) =>
        graph.FindRoute(new[] { TrackGraph.Node(start, true) }, stops.Select(s => (ICollection<int>)s).ToList(),
            null, 1_000_000f, CancellationToken.None);

    [Fact]
    public void AStopIsReachedOnATrackTheTrainCanGoOnFrom()
    {
        // Station CS has a short siding near the entry and a through track; SL lies beyond the through track.
        var graph = new TrackGraph();
        int line = graph.AddTrack("line", 1000, 0);
        int siding = graph.AddTrack("CS-B1S", 50, 0, yard: true);
        int through = graph.AddTrack("CS-A1LP", 300, 0, yard: true);
        int onward = graph.AddTrack("onward", 3000, 0);
        int sl = graph.AddTrack("SL", 50, 0);
        int loop = graph.AddTrack("loop", 15000, 0);
        graph.Connect(line, true, siding, true, 0, 0);
        graph.Connect(line, true, through, true, 0, 1);
        graph.Connect(through, true, onward, true);
        graph.Connect(onward, true, sl, true);
        // The long way round, only reachable by turning on the line.
        graph.Connect(line, false, loop, true);
        graph.Connect(loop, true, sl, true);

        var legs = Route(graph, line, new[] { siding, through }, new[] { sl });

        Assert.NotNull(legs);
        Assert.Equal(new[] { line, through }, legs[0].Select(s => s.Track));
        Assert.Equal(new[] { through, onward, sl }, legs[1].Select(s => s.Track));
        Assert.All(legs.SelectMany(l => l), s => Assert.True(s.Forward));
    }

    [Fact]
    public void TheTrainTurnsOnlyAtATerminus()
    {
        var graph = new TrackGraph();
        int line = graph.AddTrack("line", 1000, 0);
        int terminus = graph.AddTrack("HB-A1L", 200, 0, yard: true);
        int other = graph.AddTrack("other", 2000, 0);
        graph.Connect(line, true, terminus, true);
        graph.Connect(terminus, false, line, false);
        graph.Connect(line, false, other, false);

        var legs = Route(graph, line, new[] { terminus }, new[] { other });

        Assert.NotNull(legs);
        Assert.True(legs[0].Last().Forward);
        Assert.False(legs[1].First().Forward);
        Assert.Equal(new[] { terminus, line, other }, legs[1].Select(s => s.Track));
    }
}
