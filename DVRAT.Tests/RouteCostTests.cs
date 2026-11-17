using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DVRAT;
using Xunit;
using static DVRAT.ShuntingPlanner;

public class RouteCostTests
{
    private static Car ServeAt(string id, string track) =>
        new Car { Id = id, Targets = { new Target(TargetKind.Serve, track) } };

    private static Car Passengers(string id, params string[] stops)
    {
        var car = new Car { Id = id };
        for (int i = 0; i < stops.Length; i++)
            car.Targets.Add(new Target(i == stops.Length - 1 ? TargetKind.Leave : TargetKind.Board, stops[i]));
        return car;
    }

    private static Plan Solve(string[] consist, RouteCosts routes, params Car[] cars) =>
        ShuntingPlanner.Solve(consist, new Dictionary<string, List<string>>(), cars, 200000, null, CancellationToken.None, null, routes);

    private static string Stations(Plan plan) => string.Join(" ", plan.Moves.Select(m => m.Track));

    [Theory]
    // From HB, SM is near and FF far: SM first. From FF itself: FF first.
    [InlineData("HB", "SM-W1 FF-W1")]
    [InlineData("FF", "FF-W1 SM-W1")]
    public void StationsAreVisitedInTheShortestOrderFromTheStart(string start, string expected)
    {
        var routes = new RouteCosts
        {
            Start = start,
            Meters = { [("HB", "SM")] = 1000, [("HB", "FF")] = 9000, [("SM", "FF")] = 8500 },
        };

        var plan = Solve(new[] { "X", "Y" }, routes, ServeAt("X", "SM-W1"), ServeAt("Y", "FF-W1"));

        Assert.Null(plan.Error);
        Assert.Equal(expected, Stations(plan));
    }

    [Fact]
    public void PassengerStopsKeepTheirOrderEvenWhenAnotherIsNearer()
    {
        var routes = new RouteCosts
        {
            Start = "HB",
            Places = { ["AM"] = "AM", ["BR"] = "BR" },
            Meters = { [("HB", "AM")] = 9000, [("HB", "BR")] = 500, [("AM", "BR")] = 8600 },
        };

        var plan = Solve(new[] { "P1" }, routes, Passengers("P1", "AM", "BR"));

        Assert.Equal("AM BR", Stations(plan));
    }

    [Fact]
    public void RuralStopsAreSeparatePlaces()
    {
        // Two jobs with one stop each: the nearer stop comes first, which needs AM and BR to be two places.
        var routes = new RouteCosts
        {
            Start = "HB",
            Places = { ["AM"] = "AM", ["BR"] = "BR" },
            Meters = { [("HB", "AM")] = 9000, [("HB", "BR")] = 500, [("AM", "BR")] = 8600 },
        };

        var plan = Solve(new[] { "P1", "P2" }, routes, Passengers("P1", "AM"), Passengers("P2", "BR"));

        Assert.Null(plan.Error);
        Assert.Equal("BR AM", Stations(plan));
        Assert.Equal(("AM", "AM"), routes.PlaceOfTrack("AM"));
        Assert.Equal(("HB", "HB-C"), routes.PlaceOfTrack("HB-C3L"));
    }

    private static Move At(MoveKind kind, string track) => new Move { Kind = kind, Track = track };

    private static readonly HashSet<string> Known = new HashSet<string> { "HB", "SM", "FF", "AM" };

    [Fact]
    public void ARouteGoesThroughThePlansStationsInOrder()
    {
        var routes = new RouteCosts { Start = "HB", Places = { ["AM"] = "AM" } };
        var moves = new[]
        {
            At(MoveKind.PickUp, "HB-C3L"), At(MoveKind.PickUp, "HB-D1L"),
            At(MoveKind.Drop, "SM-A1L"), At(MoveKind.Serve, "AM"), At(MoveKind.Drop, "FF-B2S"), At(MoveKind.Drop, "FF-B3S"),
        };

        var route = ShuntingPlanner.RouteOf(moves, routes, Known);

        Assert.Equal("HB", route.Value.start);
        Assert.Equal(new[] { "SM", "AM" }, route.Value.stops);
        Assert.Equal("FF", route.Value.destination);
    }

    [Fact]
    public void APlanAtTheStartStationNeedsNoRoute()
    {
        var routes = new RouteCosts { Start = "HB" };

        Assert.Null(ShuntingPlanner.RouteOf(new[] { At(MoveKind.PickUp, "HB-C3L"), At(MoveKind.Drop, "HB-D1L") }, routes, Known));
    }

    [Fact]
    public void ATrainOnTheLineStartsFromItsPosition()
    {
        var routes = new RouteCosts { Start = "#Y-#S-339-#T", Places = { ["#Y-#S-339-#T"] = "#Y-#S-339-#T" } };

        var route = ShuntingPlanner.RouteOf(new[] { At(MoveKind.Drop, "SM-A1L") }, routes, Known);

        Assert.Null(route.Value.start);
        Assert.Empty(route.Value.stops);
        Assert.Equal("SM", route.Value.destination);
    }

    [Fact]
    public void DistanceIsMeasuredBetweenThePlacesNotThroughThem()
    {
        // A (station X) - L1 - L2 - B (station Y), both ways round.
        var graph = new TrackGraph();
        int a = graph.AddTrack("A", 100, 0);
        int l1 = graph.AddTrack("L1", 1000, 0);
        int l2 = graph.AddTrack("L2", 500, 0);
        int b = graph.AddTrack("B", 100, 0);
        graph.Connect(a, true, l1, true);
        graph.Connect(l1, true, l2, true);
        graph.Connect(l2, true, b, true);
        graph.Connect(b, false, l2, false);
        graph.Connect(l2, false, l1, false);
        graph.Connect(l1, false, a, false);
        int lonely = graph.AddTrack("Z", 50, 0);
        var places = new Dictionary<string, List<int>>
        {
            ["X"] = new List<int> { a },
            ["Y"] = new List<int> { b },
            ["Z"] = new List<int> { lonely },
        };

        var meters = StationDistances.Compute(graph, places, CancellationToken.None);

        Assert.Equal(1500f, meters[("X", "Y")]);
        // No way to Z: left out, so it costs a trip without distance.
        Assert.False(meters.ContainsKey(("X", "Z")));
        Assert.Equal(1500f, new RouteCosts { Meters = meters }.Distance("Y", "X"));
    }
}
