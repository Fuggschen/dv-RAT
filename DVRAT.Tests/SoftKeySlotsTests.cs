using DVRAT;
using Xunit;

public class SoftKeySlotsTests
{
    private static bool IsHalf(bool half) => half;

    [Fact]
    public void TwoHalfKeysShareASlot()
    {
        Assert.Equal(3, SoftKeySlots.Count(new[] { false, true, true, false }, IsHalf));
    }

    [Fact]
    public void ThirdHalfKeyStartsANewSlot()
    {
        Assert.Equal(2, SoftKeySlots.Count(new[] { true, true, true }, IsHalf));
    }

    [Fact]
    public void YardJobsTabFitsWithHalfPageKeys()
    {
        // Filter, Show info, Tracks, To planner, Page -, Page +: six keys in five slots.
        var keys = new[] { false, false, false, false, true, true };
        Assert.Equal(6, SoftKeySlots.Fitting(keys, IsHalf, 5));
    }

    [Fact]
    public void KeysPastTheLastSlotAreCut()
    {
        Assert.Equal(2, SoftKeySlots.Fitting(new[] { false, false, false }, IsHalf, 2));
    }

    [Fact]
    public void SecondHalfKeyStillFitsInTheLastSlot()
    {
        Assert.Equal(3, SoftKeySlots.Fitting(new[] { false, true, true, false }, IsHalf, 2));
    }
}
