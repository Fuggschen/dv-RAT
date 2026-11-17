using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DVRAT;
using Xunit;
using static DVRAT.ShuntingPlanner;

public class ShuntingPlannerTests
{
    private static Car Deliver(string id, string track) =>
        new Car { Id = id, Targets = { new Target(TargetKind.Deliver, track) } };

    private static Car NoJob(string id) => new Car { Id = id };

    private static Plan Solve(string[] consist, Dictionary<string, List<string>> waiting, params Car[] cars) =>
        ShuntingPlanner.Solve(consist, waiting ?? new Dictionary<string, List<string>>(), cars, 200000, null, CancellationToken.None);

    private static string Describe(Move m) => $"{m.Kind} {m.Track} {string.Join("+", m.Cars)}";

    [Fact]
    public void CollectsEveryJobAtAStationBeforeDrivingToTheNext()
    {
        // Three jobs from the Harbor to the Steel Mill: one by one would need two extra trips.
        var waiting = new Dictionary<string, List<string>>
        {
            ["HB-A1S"] = new List<string> { "F1" },
            ["HB-A2S"] = new List<string> { "F2" },
            ["HB-B1S"] = new List<string> { "L1" },
        };

        var plan = Solve(new string[0], waiting, Deliver("F1", "SM-C1L"), Deliver("F2", "SM-C2L"), Deliver("L1", "SM-D1S"));

        Assert.True(plan.ProvenOptimal);
        Assert.Equal("PickUp PickUp PickUp Drop Drop Drop", string.Join(" ", plan.Moves.Select(m => m.Kind)));
        // The two cars for yard C are coupled next to each other, so yard C is visited once.
        string yards = string.Join(" ", plan.Moves.Where(m => m.Kind == MoveKind.Drop).Select(m => ShuntingPlanner.PlaceOf(m.Track).yard));
        Assert.Contains(yards, new[] { "SM-C SM-C SM-D", "SM-D SM-C SM-C" });
    }

    [Fact]
    public void PlaceOfSplitsStationAndYard()
    {
        Assert.Equal(("HB", "HB-C"), ShuntingPlanner.PlaceOf("HB-C3L"));
        Assert.Equal(("", ""), ShuntingPlanner.PlaceOf("A"));
    }

    [Fact]
    public void TailAlreadyInDropOrderTakesOneMovePerTrack()
    {
        // Loco, A1, A2, B1: drop B1 at B, then A1 and A2 at A.
        var plan = Solve(new[] { "A1", "A2", "B1" }, null, Deliver("A1", "A"), Deliver("A2", "A"), Deliver("B1", "B"));

        Assert.True(plan.ProvenOptimal);
        Assert.Equal(new[] { "Drop B B1", "Drop A A1+A2" }, plan.Moves.Select(Describe));
    }

    [Fact]
    public void InterleavedDestinationsNeedThreeMoves()
    {
        // Loco, A1, B1, A2: B1 sits between the two A cars, so A has to be visited twice.
        var plan = Solve(new[] { "A1", "B1", "A2" }, null, Deliver("A1", "A"), Deliver("B1", "B"), Deliver("A2", "A"));

        Assert.True(plan.ProvenOptimal);
        Assert.Equal(3, plan.Moves.Count);
    }

    [Fact]
    public void ShuntingLoadPicksUpServesAndDelivers()
    {
        var car = new Car { Id = "C1", Targets = { new Target(TargetKind.Serve, "W"), new Target(TargetKind.Deliver, "D") } };
        var waiting = new Dictionary<string, List<string>> { ["S"] = new List<string> { "C1" } };

        var plan = Solve(new string[0], waiting, car);

        Assert.True(plan.ProvenOptimal);
        Assert.Equal(new[] { "PickUp S C1", "Serve W C1", "Drop D C1" }, plan.Moves.Select(Describe));
    }

    [Fact]
    public void TwoJobsAtOnce()
    {
        var waiting = new Dictionary<string, List<string>> { ["P"] = new List<string> { "Z1" } };

        var plan = Solve(new[] { "X1", "Y1" }, waiting, Deliver("X1", "X"), Deliver("Y1", "Y"), Deliver("Z1", "Q"));

        Assert.True(plan.ProvenOptimal);
        Assert.Equal(4, plan.Moves.Count);
        Assert.Equal("Drop Y Y1", Describe(plan.Moves[0]));
    }

    [Fact]
    public void CarsOfNoJobStayInTheTrain()
    {
        var plan = Solve(new[] { "N1", "A1" }, null, NoJob("N1"), Deliver("A1", "A"));

        Assert.Equal(new[] { "Drop A A1" }, plan.Moves.Select(Describe));
    }

    [Fact]
    public void CarOfNoJobAtTheEndIsSetAsideAndTakenBack()
    {
        // Loco, A1, N1: dropping both at A delivers A1; N1 is coupled again.
        var plan = Solve(new[] { "A1", "N1" }, null, Deliver("A1", "A"), NoJob("N1"));

        Assert.True(plan.ProvenOptimal);
        Assert.Equal(new[] { "Drop A A1+N1", "PickUp A N1" }, plan.Moves.Select(Describe));
    }

    [Fact]
    public void CarAlreadyOnItsDestinationNeedsNothing()
    {
        var waiting = new Dictionary<string, List<string>> { ["A"] = new List<string> { "A1" } };

        var plan = Solve(new string[0], waiting, Deliver("A1", "A"));

        Assert.Empty(plan.Moves);
    }

    [Fact]
    public void CarWithoutKnownPlaceIsAnError()
    {
        var plan = Solve(new string[0], null, Deliver("A1", "A"));

        Assert.NotNull(plan.Error);
        Assert.Contains("A1", plan.Error);
    }

    [Fact]
    public void GreedyPlanIsUsedWhenTheSearchLimitIsHit()
    {
        var plan = ShuntingPlanner.Solve(new[] { "A1", "B1", "A2" }, new Dictionary<string, List<string>>(),
            new[] { Deliver("A1", "A"), Deliver("B1", "B"), Deliver("A2", "A") }, 1, null, CancellationToken.None);

        Assert.False(plan.ProvenOptimal);
        Assert.Null(plan.Error);
        Assert.NotEmpty(plan.Moves);
    }

    [Fact]
    public void LegsToWarehouseBecomeServeThenDeliver()
    {
        var legs = new List<JobLeg>
        {
            new JobLeg { Kind = JobLegKind.Transport, FromTrack = "S", ToTrack = "W" },
            new JobLeg { Kind = JobLegKind.Load, ToTrack = "W" },
            new JobLeg { Kind = JobLegKind.Transport, FromTrack = "W", ToTrack = "D" },
        };

        var targets = TargetsFromLegs(legs);

        Assert.Equal(new[] { (TargetKind.Serve, "W"), (TargetKind.Deliver, "D") }, targets.Select(t => (t.Kind, t.Track)));
    }

    [Fact]
    public void PassengerStopsBecomeBoardTargets()
    {
        var legs = new List<JobLeg>
        {
            new JobLeg { Kind = JobLegKind.Unload, ToTrack = "AM", Passengers = true },
            new JobLeg { Kind = JobLegKind.Load, ToTrack = "AM", Passengers = true },
            new JobLeg { Kind = JobLegKind.Unload, ToTrack = "CW-B6LP", Passengers = true },
        };

        var targets = TargetsFromLegs(legs);

        // Alighting and boarding at AM is one stop; the job ends at CW-B6LP.
        Assert.Equal(new[] { (TargetKind.Board, "AM"), (TargetKind.Leave, "CW-B6LP") }, targets.Select(t => (t.Kind, t.Track)));
    }

    /// <summary>A car of a passenger job with these stops left, as <see cref="TargetsFromLegs"/> makes them.</summary>
    private static Car Passengers(string id, params string[] stops)
    {
        var car = new Car { Id = id };
        for (int i = 0; i < stops.Length; i++)
            car.Targets.Add(new Target(i == stops.Length - 1 ? TargetKind.Leave : TargetKind.Board, stops[i]));
        return car;
    }

    [Fact]
    public void PassengerCarsStayCoupledUntilTheLastStop()
    {
        var plan = Solve(new[] { "P1", "P2" }, null, Passengers("P1", "AM", "BR", "CW-B6LP"), Passengers("P2", "AM", "BR", "CW-B6LP"));

        Assert.Null(plan.Error);
        Assert.Equal(new[] { "Serve AM P1+P2", "Serve BR P1+P2", "Drop CW-B6LP P1+P2" }, plan.Moves.Select(Describe));
        Assert.All(plan.Moves, m => Assert.True(m.Passengers));
    }

    [Fact]
    public void FreightCarsAreNotSetAsideAtAPassengerStop()
    {
        // Setting the end car aside where the car in front of it is due would leave it at the stop.
        var plan = Solve(new[] { "P1", "F1" }, null, Passengers("P1", "AM", "CW-B6LP"), Deliver("F1", "HB-C1L"));

        Assert.Null(plan.Error);
        Assert.Equal(new[] { "Drop HB-C1L F1", "Serve AM P1", "Drop CW-B6LP P1" }, plan.Moves.Select(Describe));
    }

    [Fact]
    public void OnlyTheCarsOfAJobEndingAtAStopAreLeftThere()
    {
        // P1's job ends at AM, P2's goes on to CW-B6LP: only P1 is left at AM.
        var plan = Solve(new[] { "P2", "P1" }, null, Passengers("P1", "AM"), Passengers("P2", "AM", "CW-B6LP"));

        Assert.Null(plan.Error);
        Assert.Equal(new[] { "Drop AM P1", "Serve AM P2", "Drop CW-B6LP P2" }, plan.Moves.Select(Describe));
    }
}

public class ShuntingPlannerLimitTests
{
    [Fact]
    public void LargeProblemStaysWithinTheStateLimit()
    {
        // 12 cars for 6 tracks in mixed order plus 4 waiting cars: too many orders to try.
        var tracks = new[] { "A", "B", "C", "D", "E", "F" };
        var cars = new List<ShuntingPlanner.Car>();
        var consist = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            string id = "C" + i;
            consist.Add(id);
            cars.Add(new ShuntingPlanner.Car { Id = id, Targets = { new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Deliver, tracks[(i * 5) % 6]) } });
        }
        var waiting = new Dictionary<string, List<string>> { ["P"] = new List<string>() };
        for (int i = 0; i < 4; i++)
        {
            string id = "W" + i;
            waiting["P"].Add(id);
            cars.Add(new ShuntingPlanner.Car { Id = id, Targets = { new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Deliver, tracks[i]) } });
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = ShuntingPlanner.Solve(consist, waiting, cars, 20000, null, CancellationToken.None);

        Assert.Null(plan.Error);
        Assert.NotEmpty(plan.Moves);
        Assert.True(watch.Elapsed.TotalSeconds < 20, $"took {watch.Elapsed.TotalSeconds:F1} s");
    }

    [Fact]
    public void SearchAtTheTabletLimitStaysWellUnderAGigabyte()
    {
        // Every car out of order, so the search hits the tablet's limit.
        var tracks = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var cars = new List<ShuntingPlanner.Car>();
        var consist = new List<string>();
        for (int i = 0; i < 16; i++)
        {
            string id = "C" + i;
            consist.Add(id);
            cars.Add(new ShuntingPlanner.Car { Id = id, Targets = { new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Deliver, tracks[(i * 3) % 8]) } });
        }

        System.GC.Collect();
        long before = System.GC.GetTotalMemory(true);
        long peak = before;
        var plan = ShuntingPlanner.Solve(consist, new Dictionary<string, List<string>>(), cars, 150000,
            _ => peak = System.Math.Max(peak, System.GC.GetTotalMemory(false)), CancellationToken.None);

        Assert.Null(plan.Error);
        long usedMb = (peak - before) / (1024 * 1024);
        Assert.True(usedMb < 1024, $"used about {usedMb} MB");
    }
}

public class ShuntingPlannerBatchTests
{
    private static ShuntingPlanner.Car LoadJob(string id, string warehouse, string destination) => new ShuntingPlanner.Car
    {
        Id = id,
        Targets =
        {
            new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Serve, warehouse),
            new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Deliver, destination),
        },
    };

    [Fact]
    public void FourLoadJobsArePickedUpLoadedAndDeliveredTogether()
    {
        // Four shunting load jobs: one car each on its own track, one warehouse, four destinations.
        var waiting = new Dictionary<string, List<string>>
        {
            ["S1"] = new List<string> { "C1" }, ["S2"] = new List<string> { "C2" },
            ["S3"] = new List<string> { "C3" }, ["S4"] = new List<string> { "C4" },
        };
        var cars = new[] { LoadJob("C1", "W", "D1"), LoadJob("C2", "W", "D2"), LoadJob("C3", "W", "D3"), LoadJob("C4", "W", "D4") };

        var plan = ShuntingPlanner.Solve(new string[0], waiting, cars, 150000, null, CancellationToken.None);

        // 4 pickups, 1 stop at the warehouse, 4 drops, instead of 3 moves per job.
        Assert.True(plan.ProvenOptimal, "search did not finish");
        Assert.Equal(9, plan.Moves.Count);
        Assert.Single(plan.Moves, m => m.Kind == ShuntingPlanner.MoveKind.Serve);
    }

    [Fact]
    public void FourLoadJobsWithOtherCarsInTheTrain()
    {
        var waiting = new Dictionary<string, List<string>>
        {
            ["S1"] = new List<string> { "C1" }, ["S2"] = new List<string> { "C2" },
            ["S3"] = new List<string> { "C3" }, ["S4"] = new List<string> { "C4" },
        };
        var cars = new List<ShuntingPlanner.Car> { LoadJob("C1", "W", "D1"), LoadJob("C2", "W", "D2"), LoadJob("C3", "W", "D3"), LoadJob("C4", "W", "D4") };
        cars.AddRange(new[] { "N1", "N2", "N3" }.Select(id => new ShuntingPlanner.Car { Id = id }));

        var plan = ShuntingPlanner.Solve(new[] { "N1", "N2", "N3" }, waiting, cars, 150000, null, CancellationToken.None);

        Assert.True(plan.ProvenOptimal, "search did not finish");
        Assert.Equal(9, plan.Moves.Count);
    }

    private static Dictionary<string, List<string>> FourPickupTracks() => new Dictionary<string, List<string>>
    {
        ["S1"] = new List<string> { "C1" }, ["S2"] = new List<string> { "C2" },
        ["S3"] = new List<string> { "C3" }, ["S4"] = new List<string> { "C4" },
    };

    [Fact]
    public void FallbackPlanAlsoWorksInBatches()
    {
        var cars = new[] { LoadJob("C1", "W", "D1"), LoadJob("C2", "W", "D2"), LoadJob("C3", "W", "D3"), LoadJob("C4", "W", "D4") };

        // A limit of 1 state forces the fallback.
        var plan = ShuntingPlanner.Solve(new string[0], FourPickupTracks(), cars, 1, null, CancellationToken.None);

        Assert.False(plan.ProvenOptimal);
        Assert.Equal(new[] { "PickUp", "PickUp", "PickUp", "PickUp", "Serve" },
            plan.Moves.Take(5).Select(m => m.Kind.ToString()));
        Assert.Equal(9, plan.Moves.Count);
    }

    [Fact]
    public void ShortWarehouseTrackServesInTwoStops()
    {
        // Each car is 15 m; the warehouse track holds 30 m, so only two cars can be loaded per stop.
        var cars = new[] { LoadJob("C1", "W", "D1"), LoadJob("C2", "W", "D2"), LoadJob("C3", "W", "D3"), LoadJob("C4", "W", "D4") };
        foreach (var car in cars) car.Length = 15f;

        var plan = ShuntingPlanner.Solve(new string[0], FourPickupTracks(), cars, 150000, null, CancellationToken.None,
            new Dictionary<string, float> { ["W"] = 30f });

        var serves = plan.Moves.Where(m => m.Kind == ShuntingPlanner.MoveKind.Serve).ToList();
        Assert.Null(plan.Error);
        Assert.Equal(2, serves.Count);
        Assert.All(serves, m => Assert.True(m.Cars.Count <= 2));
    }

    [Fact]
    public void TooFullDestinationIsPlannedWithWarning()
    {
        // The destination holds one 15 m car, so the two cars can't both fit: planned anyway, with a warning.
        var cars = new[] { Car("A1", "D", 15f), Car("A2", "D", 15f) };

        var plan = ShuntingPlanner.Solve(new[] { "A1", "A2" }, new Dictionary<string, List<string>>(), cars, 150000, null,
            CancellationToken.None, new Dictionary<string, float> { ["D"] = 20f });

        Assert.Null(plan.Error);
        Assert.Single(plan.Warnings);
        Assert.Contains("D has room for 20 m", plan.Warnings[0]);
        Assert.Single(plan.Moves);
    }

    [Fact]
    public void OnlyTheServedCarsHaveToFitOnTheLoadingTrack()
    {
        // 30 m track: two 15 m job cars fit, the 40 m non-job car in front stays outside.
        var cars = new List<ShuntingPlanner.Car> { LoadJob("C1", "W", "D1"), LoadJob("C2", "W", "D1"), new ShuntingPlanner.Car { Id = "N1", Length = 40f } };
        cars[0].Length = cars[1].Length = 15f;

        var plan = ShuntingPlanner.Solve(new[] { "N1", "C1", "C2" }, new Dictionary<string, List<string>>(), cars, 150000, null,
            CancellationToken.None, new Dictionary<string, float> { ["W"] = 30f });

        Assert.Null(plan.Error);
        Assert.Equal(new[] { "Serve W C1+C2", "Drop D1 C1+C2" }, plan.Moves.Select(m => $"{m.Kind} {m.Track} {string.Join("+", m.Cars)}"));
    }

    private static ShuntingPlanner.Car Car(string id, string destination, float length) => new ShuntingPlanner.Car
    {
        Id = id, Length = length, Targets = { new ShuntingPlanner.Target(ShuntingPlanner.TargetKind.Deliver, destination) },
    };
}
