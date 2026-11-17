using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class TrainClearTests
{
    private static RouteSection Section(float start, float end, float limit) => new RouteSection { Start = start, End = end, Value = limit };

    [Fact]
    public void RisesOnceTheEndLeavesTheLowerSectionBehind()
    {
        // The front left a 30 zone 200 m ago; the 1000 m train is still in it for another 800 m.
        var behind = new[] { Section(-500, -200, 30), Section(-200, 0, 90) };
        var ahead = new[] { Section(0, 5000, 90) };

        var points = RouteProfile.TrainClearPoints(behind, ahead, 1000, 5000);

        Assert.Equal(new[] { (800f, 90f) }, points);
    }

    [Fact]
    public void ShortZonesUnderALongTrainGiveTheLimitThatThenApplies()
    {
        // 30, then 60 and 40 zones shorter than the train, then 90: the train first gets 40, then 90.
        var behind = new[] { Section(-300, 0, 30) };
        var ahead = new[] { Section(0, 200, 60), Section(200, 400, 40), Section(400, 5000, 90) };

        var points = RouteProfile.TrainClearPoints(behind, ahead, 1000, 5000);

        Assert.Equal(new[] { (1000f, 40f), (1400f, 90f) }, points);
    }

    [Fact]
    public void LimitUnderTrainIsTheLowestUnderIt()
    {
        var sections = new[] { Section(-500, -200, 30), Section(-200, 5000, 90) };

        Assert.Equal(30f, RouteProfile.LimitUnderTrain(sections, 0, 1000));
        Assert.Equal(90f, RouteProfile.LimitUnderTrain(sections, 0, 150));
    }

    [Fact]
    public void NoPointWhereTheLimitDrops()
    {
        var points = RouteProfile.TrainClearPoints(new RouteSection[0], new[] { Section(0, 100, 90), Section(100, 5000, 40) }, 300, 5000);

        Assert.Empty(points);
    }

    [Fact]
    public void EventHistoryKeepsTheLastPassedEvents()
    {
        var history = new EventHistory();
        TrackEvent Ev(string name, float at) => new TrackEvent { EventType = TrackEventType.Junction, Name = name, DistanceMeters = at };
        history.Record(0, new[] { Ev("W-1", 100), Ev("W-2", 200), Ev("W-3", 300), Ev("W-4", 900) });
        // Scanned again 350 m on: the passed switches stay, the one ahead is replaced by the new scan.
        history.Record(350, new[] { Ev("W-4", 551) });

        var passed = history.Behind(350, 2);

        Assert.Equal(new[] { ("W-2", -150f), ("W-3", -50f) }, passed.Select(e => (e.Name, e.DistanceMeters)));
    }

    [Fact]
    public void HistoryKeepsWhatTheFrontPassed()
    {
        var history = new SpeedHistory();
        history.Record(0, new[] { Section(0, 100, 30), Section(100, 5000, 90) });
        history.Record(250, new[] { Section(0, 4750, 90) });

        var behind = history.Behind(250);

        Assert.Equal(new[] { (-250f, -150f, 30f), (-150f, 0f, 90f) }, behind.Select(s => (s.Start, s.End, s.Value)));
    }
}
