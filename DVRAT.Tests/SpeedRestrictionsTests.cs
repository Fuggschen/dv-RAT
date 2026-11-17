using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class SpeedRestrictionsTests
{
    private static List<RouteSection> Signs(params (float at, float value)[] changes) =>
        RouteProfile.Sections(changes, 2000);

    [Fact]
    public void TheLowerOfSignAndRestrictionApplies()
    {
        var restrictions = new[] { new SpeedRestriction { From = 300, To = 700, LimitKmh = 40 } };

        var sections = SpeedRestrictions.Apply(Signs((0, 80), (500, 30)), restrictions, 2000);

        Assert.Equal(new float[] { 80, 40, 30 }, sections.Select(s => s.Value));
        Assert.Equal(new float[] { 0, 300, 500 }, sections.Select(s => s.Start));
    }

    [Fact]
    public void TheSignAppliesAgainBehindTheRestriction()
    {
        var restrictions = new[] { new SpeedRestriction { From = 300, To = 340, LimitKmh = 20 } };

        var sections = SpeedRestrictions.Apply(Signs((0, 80)), restrictions, 2000);

        Assert.Equal(new float[] { 80, 20, 80 }, sections.Select(s => s.Value));
        Assert.Equal(new float[] { 300, 340, 2000 }, sections.Select(s => s.End));
    }

    [Fact]
    public void WithoutASignTheRestrictionIsTheOnlyLimit()
    {
        var restrictions = new[] { new SpeedRestriction { From = 100, To = 140, LimitKmh = 50 } };

        var sections = SpeedRestrictions.Apply(new List<RouteSection>(), restrictions, 2000);

        // 0 behind it: unknown again.
        Assert.Equal(new float[] { 50, 0 }, sections.Select(s => s.Value));
        Assert.Equal(new float[] { 100, 140 }, sections.Select(s => s.Start));
    }

    [Fact]
    public void ARestrictionTheCarIsInAppliesFromTheCar()
    {
        var restrictions = new[] { new SpeedRestriction { From = -30, To = 10, LimitKmh = 0 } };

        var sections = SpeedRestrictions.Apply(Signs((0, 80)), restrictions, 2000);

        Assert.Equal(new float[] { 0, 80 }, sections.Select(s => s.Value));
        Assert.Equal(0, SpeedRestrictions.AtCar(restrictions).LimitKmh);
    }

    [Fact]
    public void NoRestrictionsLeaveTheSectionsAlone()
    {
        var signs = Signs((0, 80), (500, 60));

        var sections = SpeedRestrictions.Apply(signs, new SpeedRestriction[0], 2000);

        Assert.Equal(signs.Select(s => (s.Start, s.End, s.Value)), sections.Select(s => (s.Start, s.End, s.Value)));
    }

    [Fact]
    public void NeighbouringSectionsBecomeOneRestriction()
    {
        var sections = new[]
        {
            new SpeedRestriction { From = 140, To = 160, LimitKmh = 30, DamagePercent = 22 },
            new SpeedRestriction { From = 100, To = 120, LimitKmh = 60, DamagePercent = 6 },
            new SpeedRestriction { From = 120, To = 140, LimitKmh = 40, DamagePercent = 17 },
            new SpeedRestriction { From = 900, To = 920, LimitKmh = 50, DamagePercent = 11 },
            new SpeedRestriction { From = -80, To = -60, LimitKmh = 10, DamagePercent = 31 },
        };

        var merged = SpeedRestrictions.Merge(sections, 50);

        Assert.Equal(new[] { (100f, 160f, 30, 22), (900f, 920f, 50, 11) },
            merged.Select(r => (r.From, r.To, r.LimitKmh, r.DamagePercent)));
    }
}
