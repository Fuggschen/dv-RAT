using System.Collections.Generic;
using DVRAT;
using Xunit;

public class NavigatorTests
{
    [Fact]
    public void StartsAtHomeWithoutHistory()
    {
        var nav = new Navigator("home");

        Assert.Equal("home", nav.Current);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void BackReturnsToPreviousPage()
    {
        var nav = new Navigator("home");
        nav.Home();
        nav.Open("train");
        nav.Open("loco:DE2");

        nav.Back();

        Assert.Equal("train", nav.Current);
    }

    [Fact]
    public void OpeningCurrentPageAddsNoHistory()
    {
        var nav = new Navigator("home");
        nav.Home();
        nav.Open("train");
        nav.Open("train");

        nav.Back();

        Assert.Equal("home", nav.Current);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void ReplaceSwapsCurrentPageSoBackSkipsIt()
    {
        var nav = new Navigator("home");
        nav.Home();
        nav.Open("train");
        nav.Replace("route");

        nav.Back();

        Assert.Equal("home", nav.Current);
    }

    [Fact]
    public void HomeClearsHistory()
    {
        var nav = new Navigator("home");
        nav.Open("a");
        nav.Open("b");

        nav.Home();

        Assert.Equal("home", nav.Current);
        Assert.False(nav.CanGoBack);
    }

    [Fact]
    public void ChangedReportsEveryNewPage()
    {
        var nav = new Navigator("home");
        var seen = new List<string>();
        nav.Changed += seen.Add;

        nav.Home();
        nav.Open("a");
        nav.Back();

        Assert.Equal(new[] { "home", "a", "home" }, seen);
    }
}

public class NavigatorRestoreTests
{
    [Fact]
    public void RestoreOpensTheLastPageAndKeepsBack()
    {
        var navigator = new Navigator("home");
        string shown = null;
        navigator.Changed += id => shown = id;

        navigator.Restore(new[] { "home", "train", "loco" });

        Assert.Equal("loco", shown);
        navigator.Back();
        Assert.Equal("train", navigator.Current);
    }

    [Fact]
    public void OnlyGoingSomewhereCountsAsNavigating()
    {
        var navigator = new Navigator("home");
        var went = new System.Collections.Generic.List<string>();
        navigator.Navigated += went.Add;

        navigator.Restore(new[] { "home", "train" });
        navigator.Open("yard");
        navigator.Replace("jobs");
        navigator.Back();
        navigator.Home();

        Assert.Equal(new[] { "yard", "jobs", "train", "home" }, went);
    }

    [Fact]
    public void RestoreWithNothingSavedOpensHome()
    {
        var navigator = new Navigator("home");

        navigator.Restore(new string[0]);

        Assert.Equal("home", navigator.Current);
        Assert.False(navigator.CanGoBack);
    }
}
