using System.Collections.Generic;
using DVRAT;
using Xunit;

public class NavigatorTests
{
    private static readonly Dictionary<string, string> Parents = new Dictionary<string, string>
    {
        ["train"] = "home", ["loco"] = "train", ["supplies"] = "loco", ["car_job"] = "train",
        ["world"] = "home", ["loco_ports"] = "world", ["rollingstock"] = "info", ["car:DE2"] = "rollingstock", ["info"] = "home",
    };

    private static string ParentOf(string id) => Parents.TryGetValue(id, out string parent) ? parent : null;

    /// <summary>The pages that reset the loco chosen by opening "loco", as RATUIManager does it.</summary>
    private static List<string> Resets(params string[] steps)
    {
        var nav = new Navigator("home");
        var resets = new List<string>();
        bool chosen = false;
        nav.Navigated += id =>
        {
            if (chosen && Navigator.Leaves(id, "loco", nav.History, ParentOf))
            {
                chosen = false;
                resets.Add(id);
            }
        };
        nav.Home();
        foreach (string step in steps)
        {
            if (step == "back")
            {
                nav.Back();
                continue;
            }
            chosen |= step == "loco";
            nav.Open(step);
        }
        return resets;
    }

    [Fact]
    public void ChosenLocoStaysOnItsSubpagesAndLinksOut()
    {
        Assert.Empty(Resets("train", "loco", "supplies", "loco_ports", "back", "back", "car:DE2", "back"));
    }

    [Fact]
    public void ChosenLocoResetsWhenTrainOrHomeIsReached()
    {
        Assert.Equal(new[] { "train" }, Resets("train", "loco", "back"));
        Assert.Equal(new[] { "train" }, Resets("train", "loco", "supplies", "train"));
        Assert.Equal(new[] { "home" }, Resets("train", "loco", "home"));
        Assert.Equal(new[] { "train" }, Resets("train", "loco", "car:DE2", "train"));
    }

    [Fact]
    public void LeavesChecksTheTargetAgainstThePage()
    {
        var history = new List<string> { "home", "world" };

        Assert.True(Navigator.Leaves("world", "loco", history, ParentOf));
        Assert.False(Navigator.Leaves("supplies", "loco", history, ParentOf));
        Assert.True(Navigator.IsWithin("supplies", "train", ParentOf));
    }

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
