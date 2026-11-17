using DVRAT;
using Xunit;

public class TrackStretchesTests
{
    [Fact]
    public void SamplesJoinIntoStretches()
    {
        var samples = new[] { false, true, true, false, true };

        var stretches = TrackStretches.FromSamples(samples, 2f, 9f);

        Assert.Equal(new[] { (2f, 6f), (8f, 9f) }, stretches);
    }

    [Fact]
    public void ASectionOverATrackEndIsOne()
    {
        // 100 to 140 m on one track, 141 to 230 m on the next.
        var points = TrackStretches.Points(new[] { (141f, 230f), (100f, 140f) }, 5000f, 4f);

        Assert.Equal(new[] { (100f, true), (230f, false) }, points);
    }

    [Fact]
    public void AShortGapInTheWireDoesNotEndIt()
    {
        // Under wire from behind the car: a 50 m gap at a switch, then the wire ends 70 m before it begins again.
        var points = TrackStretches.Points(new[] { (-100f, 400f), (450f, 900f), (970f, 2000f) }, 5000f, 60f);

        Assert.Equal(new[] { (900f, false), (970f, true), (2000f, false) }, points);
    }

    [Fact]
    public void RunsSplitWhereTheValueChanges()
    {
        var runs = TrackStretches.Runs(new[] { 0, 1500, 1500, 750, 0, 750 }, 2f, 11f);

        Assert.Equal(new[] { (2f, 6f, 1500), (6f, 8f, 750), (10f, 11f, 750) }, runs);
    }

    [Fact]
    public void TheVoltageIsThatOfTheFirstFedWireAhead()
    {
        // A neutral stretch from 100 to 130 m before the 750 V line.
        var fed = new[] { (-500f, 90f, 1500), (130f, 900f, 750) };

        Assert.Equal(750, TrackStretches.ValueAfter(fed, 100f, 60f));
        Assert.Equal(0, TrackStretches.ValueAfter(fed, 950f, 60f));
    }

    [Fact]
    public void InsideASectionOnlyItsEndIsAhead()
    {
        var points = TrackStretches.Points(new[] { (-30f, 50f) }, 5000f, 4f);

        Assert.Equal(new[] { (50f, false) }, points);
    }

    [Fact]
    public void ASectionPastTheScanHasNoEnd()
    {
        var points = TrackStretches.Points(new[] { (-80f, -10f), (4900f, 5100f) }, 5000f, 4f);

        Assert.Equal(new[] { (4900f, true) }, points);
    }
}
