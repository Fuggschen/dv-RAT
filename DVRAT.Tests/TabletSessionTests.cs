using System.Collections.Generic;
using DVRAT;
using Newtonsoft.Json.Linq;
using Xunit;

public class TabletSessionTests
{
    [Fact]
    public void SurvivesASaveRoundTrip()
    {
        var session = new TabletSession { PoweredOn = true, History = { "home", "yard" }, PlannerFrom = "HB", PlannerTo = "SM", PlannerHasRoute = true };
        session.Vars("yard", new Dictionary<string, string> { ["sort"] = "item.pay" })["sort"] = "item.massT";
        session.Stopwatch.Start(10f);
        session.Stopwatch.Checkpoint(25f);

        var loaded = TabletSession.FromJson(JToken.Parse(session.ToJson().ToString()));

        Assert.Equal(session.Id, loaded.Id);
        Assert.True(loaded.PoweredOn);
        Assert.Equal(new[] { "home", "yard" }, loaded.History);
        Assert.Equal("item.massT", loaded.Vars("yard", new Dictionary<string, string> { ["sort"] = "item.pay" })["sort"]);
        Assert.Equal(("HB", "SM", true), (loaded.PlannerFrom, loaded.PlannerTo, loaded.PlannerHasRoute));
        Assert.True(loaded.Stopwatch.Running);
        // A running watch continues from its saved time once the new session's clock is known.
        Assert.Equal(15f, loaded.Stopwatch.Elapsed(100f));
        loaded.Stopwatch.Resume(100f);
        Assert.Equal(20f, loaded.Stopwatch.Elapsed(105f));
    }

    [Fact]
    public void ViewStateIsNotSavedAndListsResetAlone()
    {
        var session = new TabletSession();
        session.View("tracks")["track"] = "A1";
        session.View("tracks")[TabletSession.ListKeyPrefix + "cars"] = "A1|2";

        session.ResetLists("tracks");

        Assert.Equal(new Dictionary<string, string> { ["track"] = "A1" }, session.View("tracks"));
        Assert.Empty(TabletSession.FromJson(JToken.Parse(session.ToJson().ToString())).ViewVars);
    }

    [Fact]
    public void SyncCarriesWhatIsShownButNotTheTabletsIdOrTheViewersFolder()
    {
        var host = new TabletSession { PoweredOn = true, History = { "home", "media" } };
        host.Vars("media", null)["mediaFolder"] = "C:/Host/Music";
        host.Vars("media", null)["sort"] = "name";
        host.View("tracks")["track"] = "A1";
        host.Settings["theme"] = "dark";
        host.Settings["pcAudioApp"] = "Spotify|Spotify";
        host.Stopwatch.Start(10f);
        host.Stopwatch.Checkpoint(20f);

        var client = new TabletSession();
        client.Vars("media", null)["mediaFolder"] = "C:/Client/Music";
        client.Settings["pcAudioApp"] = "firefox|Firefox";
        string clientId = client.Id;
        client.ApplySync(JObject.Parse(host.ToSyncJson().ToString()), 100f);

        Assert.Equal(clientId, client.Id);
        Assert.True(client.PoweredOn);
        Assert.Equal(new[] { "home", "media" }, client.History);
        Assert.Equal("name", client.Vars("media", null)["sort"]);
        Assert.Equal("C:/Client/Music", client.Vars("media", null)["mediaFolder"]);
        Assert.Equal("A1", client.View("tracks")["track"]);
        Assert.Equal("dark", client.Settings["theme"]);
        Assert.Equal("firefox|Firefox", client.Settings["pcAudioApp"]);
        // The running watch goes on from the time it had, on the client's clock.
        Assert.Equal(15f, client.Stopwatch.Elapsed(105f));
        Assert.DoesNotContain("C:/Host/Music", host.ToSyncJson().ToString());
    }

    [Fact]
    public void EveryTabletGetsItsOwnId()
    {
        Assert.NotEqual(new TabletSession().Id, new TabletSession().Id);
        // Saves from before the ID get one when loaded.
        Assert.False(string.IsNullOrEmpty(TabletSession.FromJson(JObject.Parse("{\"on\": true}")).Id));
    }

    [Fact]
    public void MissingStateGivesAFreshSession()
    {
        var session = TabletSession.FromJson(null);

        Assert.False(session.PoweredOn);
        Assert.Empty(session.History);
        Assert.Equal(0f, session.Stopwatch.Elapsed(50f));
    }

    [Fact]
    public void VarsKeepSavedValuesAndAddNewDefaults()
    {
        var session = new TabletSession();
        session.Vars("yard", new Dictionary<string, string> { ["tab"] = "jobs" })["tab"] = "tracks";

        var vars = session.Vars("yard", new Dictionary<string, string> { ["tab"] = "jobs", ["sort"] = "item.pay" });

        Assert.Equal("tracks", vars["tab"]);
        Assert.Equal("item.pay", vars["sort"]);
    }

    [Fact]
    public void StopwatchStopsAndResets()
    {
        var watch = new Stopwatch();
        watch.Start(0f);
        watch.Stop(30f);
        Assert.Equal(30f, watch.Elapsed(100f));

        watch.Reset(100f);
        Assert.Equal(0f, watch.Elapsed(120f));
    }

    [Fact]
    public void SettingsFallBackToTheirDefaultsAndAreSaved()
    {
        var session = new TabletSession();
        Assert.Equal("true", session.Setting("showSignalAspects"));
        Assert.Equal("true", session.Setting("showSwitchDamage"));
        Assert.Equal("true", session.Setting("showTrackDamage"));

        session.Settings["showSignalAspects"] = "false";
        var loaded = TabletSession.FromJson(JToken.Parse(session.ToJson().ToString()));

        Assert.Equal("false", loaded.Setting("showSignalAspects"));
        Assert.Equal("false", loaded.AllSettings()["showSignalAspects"]);
        Assert.Equal("", loaded.Setting("unknown"));
    }
}
