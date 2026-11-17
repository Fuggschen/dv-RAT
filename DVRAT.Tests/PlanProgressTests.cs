using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;
using static DVRAT.ShuntingPlanner;

public class PlanProgressTests
{
    private static Move Step(MoveKind kind, string track, params string[] cars) =>
        new Move { Kind = kind, Track = track, Cars = cars.ToList() };

    // Couple A1+A2 on HB-A1S and B1 on HB-B1S, then drop B1 and A1+A2 at the Steel Mill.
    private static readonly List<Move> Moves = new List<Move>
    {
        Step(MoveKind.PickUp, "HB-A1S", "A1", "A2"),
        Step(MoveKind.PickUp, "HB-B1S", "B1"),
        Step(MoveKind.Drop, "SM-B1L", "B1"),
        Step(MoveKind.Drop, "SM-A1L", "A1", "A2"),
    };

    private static readonly Dictionary<string, string> Tracks = new Dictionary<string, string>();

    private static bool Update(PlanProgress progress, params string[] train) =>
        progress.Update(train, _ => false, car => Tracks.TryGetValue(car, out string t) ? t : null);

    [Fact]
    public void FollowsTheStepsAsTheTrainChanges()
    {
        var progress = new PlanProgress(new string[0], Moves);

        Assert.False(Update(progress));
        Update(progress, "A1");
        Assert.Equal(0, progress.Current);
        Update(progress, "A1", "A2");
        Assert.Equal(1, progress.Current);
        Update(progress, "A1", "A2", "B1");
        Update(progress, "A1", "A2");

        Assert.Equal(3, progress.Current);
        Assert.Equal(new[] { PlanProgress.StepState.Done, PlanProgress.StepState.Done, PlanProgress.StepState.Done, PlanProgress.StepState.Pending },
            progress.States);
    }

    [Fact]
    public void CatchesUpOnSeveralStepsAtOnce()
    {
        var progress = new PlanProgress(new string[0], Moves);

        Update(progress, "A1", "A2", "B1");

        Assert.Equal(2, progress.Current);
        Assert.False(progress.Failed);
    }

    [Fact]
    public void CouplingInTheWrongOrderFailsTheStep()
    {
        var progress = new PlanProgress(new string[0], Moves);

        Assert.True(Update(progress, "B1"));

        Assert.True(progress.Failed);
        Assert.Equal(PlanProgress.StepState.Failed, progress.States[0]);
        // Stays failed until a new plan is made.
        Assert.False(Update(progress, "A1", "A2"));
    }

    [Fact]
    public void DroppingOnTheWrongTrackFailsTheStep()
    {
        var progress = new PlanProgress(new[] { "A1", "A2", "B1" }, Moves.Skip(2).ToList());
        Tracks["B1"] = "SM-X9S";

        Update(progress, "A1", "A2");

        Tracks.Remove("B1");
        Assert.True(progress.Failed);
    }

    [Fact]
    public void LoadStepWaitsForTheWarehouse()
    {
        var moves = new List<Move> { Step(MoveKind.Serve, "HB-D7L", "C1"), Step(MoveKind.Drop, "HB-C1S", "C1") };
        var progress = new PlanProgress(new[] { "C1" }, moves);
        bool loaded = false;

        progress.Update(new[] { "C1" }, _ => loaded, _ => null);
        Assert.Equal(0, progress.Current);
        loaded = true;
        progress.Update(new[] { "C1" }, _ => loaded, _ => null);

        Assert.Equal(1, progress.Current);
    }
}
