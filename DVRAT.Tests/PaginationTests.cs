using DVRAT;
using Xunit;

public class PaginationTests
{
    [Fact]
    public void SingleRowsFillScreens()
    {
        Assert.Equal(new[] { 0, 3, 6 }, Pagination.ScreenStarts(new[] { 1, 1, 1, 1, 1, 1, 1 }, 3));
    }

    [Fact]
    public void GroupThatDoesNotFitMovesToNextScreen()
    {
        // Caption + 2 link rows after 2 single rows on a 4-row screen: the group starts screen 2.
        Assert.Equal(new[] { 0, 2 }, Pagination.ScreenStarts(new[] { 1, 1, 3 }, 4));
    }

    [Fact]
    public void GroupThatFitsStaysOnScreen()
    {
        Assert.Equal(new[] { 0 }, Pagination.ScreenStarts(new[] { 1, 3 }, 4));
    }

    [Fact]
    public void GroupLongerThanAScreenIsSplit()
    {
        Assert.Equal(new[] { 0, 1, 4, 7 }, Pagination.ScreenStarts(new[] { 1, 8 }, 3));
    }

    [Fact]
    public void NoRowsIsOneEmptyScreen()
    {
        Assert.Equal(new[] { 0 }, Pagination.ScreenStarts(new int[0], 5));
    }
}
