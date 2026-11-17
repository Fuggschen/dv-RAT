using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class RouteProfileTests
{
    [Fact]
    public void EachChangeAppliesUntilTheNext()
    {
        var sections = RouteProfile.Sections(new List<(float, float)> { (0, 80), (500, 60), (900, 100) }, 2000);

        Assert.Equal(new float[] { 80, 60, 100 }, sections.Select(s => s.Value));
        Assert.Equal(new float[] { 0, 500, 900 }, sections.Select(s => s.Start));
        Assert.Equal(new float[] { 500, 900, 2000 }, sections.Select(s => s.End));
    }

    [Fact]
    public void RepeatedValuesDoNotStartNewSections()
    {
        var sections = RouteProfile.Sections(new List<(float, float)> { (0, 80), (300, 80), (600, 60), (700, 60) }, 1000);

        Assert.Equal(new float[] { 80, 60 }, sections.Select(s => s.Value));
        Assert.Equal(600, sections[1].Start);
    }

    [Fact]
    public void ChangesAreSortedByDistance()
    {
        var sections = RouteProfile.Sections(new List<(float, float)> { (400, 40), (0, 120) }, 800);

        Assert.Equal(new float[] { 120, 40 }, sections.Select(s => s.Value));
    }

    [Fact]
    public void LaterChangeAtSameSpotWins()
    {
        // A track sign and a junction sign at the same point: the junction sign is added later.
        var sections = RouteProfile.Sections(new List<(float, float)> { (0, 80), (500, 100), (500, 40) }, 1000);

        Assert.Equal(new float[] { 80, 40 }, sections.Select(s => s.Value));
    }

    [Fact]
    public void NoChangesGiveNoSections()
    {
        Assert.Empty(RouteProfile.Sections(new List<(float, float)>(), 1000));
    }

    [Fact]
    public void AtFindsSectionByDistance()
    {
        var sections = RouteProfile.Sections(new List<(float, float)> { (0, 1), (100, 2) }, 200);

        Assert.Equal(1, RouteProfile.At(sections, 50).Value);
        Assert.Equal(2, RouteProfile.At(sections, 150).Value);
        Assert.Equal(2, RouteProfile.At(sections, 500).Value);
    }

    [Fact]
    public void TravelTimeUsesTheLimitsAndAssumesASpeedWhereNoneIsKnown()
    {
        var sections = new List<RouteSection>
        {
            new RouteSection { Start = 0, End = 1000, Value = 36 },   // 10 m/s
            new RouteSection { Start = 1000, End = 2000, Value = 0 },  // unknown: 40 km/h
        };

        Assert.Equal(100, RouteProfile.TravelSeconds(sections, 1000), 3);
        Assert.Equal(100 + 90, RouteProfile.TravelSeconds(sections, 2000), 3);
        // Past the scanned sections the unknown speed applies too.
        Assert.Equal(100 + 180, RouteProfile.TravelSeconds(sections, 3000), 3);
    }
}

