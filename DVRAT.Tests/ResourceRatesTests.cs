using System.Collections.Generic;
using DVRAT;
using Xunit;

public class ResourceRatesTests
{
    private static Dictionary<string, ResourceLevel> Fuel(float amount) =>
        new Dictionary<string, ResourceLevel> { ["fuel"] = new ResourceLevel { Amount = amount, Capacity = 1000 } };

    private static float? Rate(ResourceRates rates, string locoId, float amount, float seconds)
    {
        var levels = Fuel(amount);
        rates.Update(locoId, levels, seconds);
        return levels["fuel"].PerMinute;
    }

    [Fact]
    public void RateIsTheDropSinceTheLastReadingPerMinute()
    {
        var rates = new ResourceRates();

        Assert.Null(Rate(rates, "L-1", 500, 10));
        Assert.Equal(30f, Rate(rates, "L-1", 499, 12));
    }

    [Fact]
    public void RisingLevelHasNoRate()
    {
        var rates = new ResourceRates();
        Rate(rates, "L-1", 500, 10);

        Assert.Null(Rate(rates, "L-1", 600, 12));
        Assert.Equal(60f, Rate(rates, "L-1", 598, 14));
    }

    [Fact]
    public void QuickSecondReadingKeepsTheRate()
    {
        var rates = new ResourceRates();
        Rate(rates, "L-1", 500, 10);
        Rate(rates, "L-1", 499, 12);

        Assert.Equal(30f, Rate(rates, "L-1", 499, 12.1f));
        Assert.Equal(30f, Rate(rates, "L-1", 498, 14));
    }

    [Fact]
    public void LongGapOrOtherLocoStartsOver()
    {
        var rates = new ResourceRates();
        Rate(rates, "L-1", 500, 10);

        Assert.Null(Rate(rates, "L-1", 400, 100));
        Assert.Null(Rate(rates, "L-2", 390, 102));
        Assert.Equal(30f, Rate(rates, "L-2", 389, 104));
    }
}
