using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

// Readings built in the shapes RealisticFiring.Api.Read documents: Dictionary<string, object> sections, List<object> lists, floats.
public class FiringDataTests
{
    private static Dictionary<string, object> Coal() => new Dictionary<string, object>
    {
        ["fuel"] = "Coal",
        ["fire"] = new Dictionary<string, object>
        {
            ["lit"] = true, ["heatPct"] = 72f, ["level"] = "Ok", ["draftPct"] = 95f, ["draftLevel"] = "Ok",
            ["smokePct"] = 30f, ["temperatureC"] = 1100f,
        },
        ["grate"] = new Dictionary<string, object>
        {
            ["coalKg"] = 180f, ["ashpanPct"] = 40f, ["ashpanFull"] = false, ["clinkerKg"] = 3f, ["needsCleaning"] = false,
            ["doorPct"] = 0f, ["thinZone"] = "FrontLeft", ["zoneReferenceKg"] = 20f,
            ["zones"] = Enumerable.Range(0, 9).Select(i => (object)new Dictionary<string, object>
            {
                ["name"] = "Zone" + i, ["coalKg"] = 20f + i, ["burningPct"] = 50f, ["clinkerKg"] = 0.5f,
            }).ToList(),
        },
        ["coal"] = new Dictionary<string, object> { ["moisturePct"] = 4f, ["condition"] = "Damp" },
        ["boiler"] = new Dictionary<string, object>
        {
            ["pressureBar"] = 13f, ["workingBar"] = 14f, ["pressurePct"] = 92.8f, ["pressureLevel"] = "Ok",
            ["glassPct"] = 55f, ["glassLevel"] = "Ok", ["waterC"] = 195f, ["heating"] = false,
            ["sootLossPct"] = 2f, ["scaleLossPct"] = 1f,
            ["plug"] = new Dictionary<string, object> { ["melted"] = false, ["coreC"] = 210f },
        },
        ["injectors"] = new List<object>
        {
            new Dictionary<string, object>
            {
                ["name"] = "right", ["state"] = "Working", ["waterKgPerS"] = 2.5f, ["tenderShut"] = false,
                ["steamValve"] = "Feed", ["waterValvePct"] = 60f,
            },
            new Dictionary<string, object>
            {
                ["name"] = "left", ["state"] = "Off", ["waterKgPerS"] = 0f, ["tenderShut"] = true, ["leverPct"] = 0f,
            },
        },
        ["overflow"] = new Dictionary<string, object> { ["waterKgPerS"] = 0.1f, ["steamKgPerS"] = 0.02f },
        ["engine"] = new Dictionary<string, object>
        {
            ["cutoffPct"] = 35f, ["steamChestBar"] = 11f, ["meanPressureBar"] = 5f, ["steamKgPerS"] = 3f,
            ["condensingPct"] = 4f, ["superheated"] = true, ["superheatC"] = 90f, ["steamC"] = 290f,
        },
        ["fireman"] = new Dictionary<string, object> { ["mode"] = "Road", ["call"] = "HeavyAhead", ["callLeftS"] = 40f },
        ["advice"] = new Dictionary<string, object> { ["level"] = "Watch", ["text"] = "Fire the front left" },
    };

    private static Dictionary<string, object> Oil() => new Dictionary<string, object>
    {
        ["fuel"] = "Oil",
        ["fire"] = new Dictionary<string, object>
        {
            ["lit"] = false, ["heatPct"] = 0f, ["level"] = "Act", ["draftPct"] = 0f, ["draftLevel"] = "Ok", ["smokePct"] = 0f,
        },
        ["burner"] = new Dictionary<string, object>
        {
            ["firingValvePct"] = 0f, ["feedKgPerS"] = 0f, ["atomizerPct"] = 20f, ["atomizingPct"] = 0f, ["oilC"] = 15f,
            ["heaterPct"] = 0f, ["brickC"] = 15f, ["onShedSteam"] = true, ["steamLinePct"] = 100f, ["flueSootPct"] = 0f,
            ["lighting"] = new List<object>
            {
                new Dictionary<string, object> { ["title"] = "Steam", ["how"] = "Open the shed line", ["done"] = true, ["current"] = false },
                new Dictionary<string, object> { ["title"] = "Atomizer", ["how"] = "Crack it open", ["done"] = false, ["current"] = true },
            },
        },
        ["boiler"] = new Dictionary<string, object>
        {
            ["pressureBar"] = 0f, ["workingBar"] = 14f, ["pressurePct"] = 0f, ["pressureLevel"] = "Act",
            ["glassPct"] = 60f, ["glassLevel"] = "Ok",
        },
        ["injectors"] = new List<object>(),
        ["advice"] = new Dictionary<string, object> { ["level"] = "Act", ["text"] = "Light the fire" },
    };

    [Fact]
    public void ReadsCoalEngine()
    {
        var data = FiringData.FromReadings(Coal());

        Assert.True(data.IsCoal);
        Assert.True(data.Fire.Lit);
        Assert.Equal(72f, data.Fire.HeatPct);
        Assert.Equal(1100f, data.Fire.TemperatureC);
        Assert.Equal(9, data.Grate.Zones.Count);
        Assert.Equal(28f, data.Grate.Zones[8].CoalKg);
        Assert.Equal("FrontLeft", data.Grate.ThinZone);
        Assert.Equal(0f, data.Grate.DoorPct);
        Assert.Equal("Damp", data.Coal.Condition);
        Assert.Null(data.Coal.CoverOpen);
        Assert.Equal(13f, data.Boiler.PressureBar);
        Assert.False(data.Boiler.PlugMelted);
        Assert.Equal(210f, data.Boiler.PlugCoreC);
        Assert.Null(data.Boiler.AlarmSounding);
        Assert.Null(data.Boiler.ImpuritiesPpm);
        Assert.Equal(2, data.Injectors.Count);
        Assert.Equal("Feed", data.Injectors[0].SteamValve);
        Assert.Null(data.Injectors[0].LeverPct);
        Assert.Null(data.Injectors[1].SteamValve);
        Assert.Equal(0f, data.Injectors[1].LeverPct);
        Assert.True(data.Injectors[1].TenderShut);
        Assert.Equal(0.02f, data.Overflow.SteamKgPerS);
        Assert.Equal(290f, data.Engine.SteamC);
        Assert.Equal("HeavyAhead", data.Fireman.Call);
        Assert.Equal("Watch", data.AdviceLevel);
        Assert.Equal("Fire the front left", data.AdviceText);
    }

    [Fact]
    public void ReadsOilEngineWithoutCoalSections()
    {
        var data = FiringData.FromReadings(Oil());

        Assert.False(data.IsCoal);
        Assert.Null(data.Fire.TemperatureC);
        Assert.Null(data.Grate);
        Assert.Null(data.Coal);
        Assert.Null(data.Stoker);
        Assert.Null(data.Engine);
        Assert.Null(data.Fireman);
        Assert.Null(data.Boiler.WaterC);
        Assert.Null(data.Burner.StopPct);
        Assert.True(data.Burner.OnShedSteam);
        Assert.Equal(2, data.Burner.Lighting.Count);
        Assert.True(data.Burner.Lighting[1].Current);
        Assert.Equal("Crack it open", data.Burner.Lighting[1].How);
        Assert.Empty(data.Injectors);
    }

    [Fact]
    public void NoSnapshotWithoutFuel()
    {
        Assert.Null(FiringData.FromReadings(null));
        Assert.Null(FiringData.FromReadings(new Dictionary<string, object>()));
        Assert.Null(FiringData.FromReadings(new Dictionary<string, object> { ["fuel"] = 1f }));
    }

    [Fact]
    public void WrongTypesReadAsMissing()
    {
        var readings = Coal();
        var fire = (Dictionary<string, object>)readings["fire"];
        fire["heatPct"] = "hot";
        fire["lit"] = 1f;
        fire["temperatureC"] = null;
        readings["grate"] = "not a section";
        readings["injectors"] = new List<object> { "not an injector" };
        readings.Remove("advice");

        var data = FiringData.FromReadings(readings);

        Assert.Equal(0f, data.Fire.HeatPct);
        Assert.False(data.Fire.Lit);
        Assert.Null(data.Fire.TemperatureC);
        Assert.Null(data.Grate);
        Assert.Empty(data.Injectors);
        Assert.Equal("Ok", data.AdviceLevel);
        Assert.Equal("", data.AdviceText);
    }

    private static Dictionary<string, object> Bound(FiringData firing)
    {
        var loco = new LocoInfo { IsLocomotive = true, Firing = firing };
        var root = PageBindings.Build(new TrainData { Cars = { loco }, Locomotive = loco }, null, null, null,
            new Dictionary<string, string>(), id => id);
        return (Dictionary<string, object>)root["firing"];
    }

    private static Dictionary<string, object> Part(Dictionary<string, object> section, string key) =>
        (Dictionary<string, object>)section[key];

    [Fact]
    public void BindsCoalEngine()
    {
        var firing = Bound(FiringData.FromReadings(Coal()));

        Assert.True((bool)firing["present"]);
        Assert.True((bool)firing["coal"]);
        Assert.Equal("lit", Part(firing, "fire")["state"]);
        Assert.Equal("watch", Part(firing, "advice")["level"]);
        Assert.True((bool)Part(firing, "advice")["levelWatch"]);
        Assert.False((bool)Part(firing, "advice")["levelAct"]);
        Assert.Equal("front left", Part(firing, "grate")["thinZone"]);
        var zones = (List<object>)Part(firing, "grate")["zones"];
        Assert.Equal(140f, (float)((Dictionary<string, object>)zones[8])["bedPct"], 3);
        var rows = ((List<object>)Part(firing, "grate")["rows"]).Cast<Dictionary<string, object>>().ToList();
        Assert.Equal(new[] { "front", "middle", "back" }, rows.Select(r => (string)r["row"]));
        Assert.Equal("26 kg, 50 %", rows[0]["left"]);
        Assert.Equal("20 kg, 50 %", rows[2]["left"]);
        var injectors = (List<object>)firing["injectors"];
        Assert.Equal("steam feed, water 60 %", ((Dictionary<string, object>)injectors[0])["setting"]);
        Assert.Equal("lever 0 %", ((Dictionary<string, object>)injectors[1])["setting"]);
        Assert.Equal("heavy ahead", Part(firing, "fireman")["call"]);
        Assert.True((bool)Part(firing, "fireman")["hasCall"]);
    }

    [Fact]
    public void ClampsBarsButKeepsValues()
    {
        var readings = Coal();
        ((Dictionary<string, object>)readings["fire"])["heatPct"] = 180f;
        ((Dictionary<string, object>)readings["fire"])["draftPct"] = 400f;

        var fire = Part(Bound(FiringData.FromReadings(readings)), "fire");

        Assert.Equal(180f, fire["heatPct"]);
        Assert.Equal(100f, fire["heatBarPct"]);
        Assert.Equal(250f, fire["draftBarPct"]);
    }

    [Fact]
    public void BindsEveryKeyWithoutTheMod()
    {
        var oil = Bound(FiringData.FromReadings(Oil()));
        var none = Bound(null);

        Assert.False((bool)none["present"]);
        foreach (var key in oil.Keys)
            Assert.True(none.ContainsKey(key), key);
        foreach (var section in oil.Where(e => e.Value is Dictionary<string, object>))
            Assert.Equal(((Dictionary<string, object>)section.Value).Keys.OrderBy(k => k),
                ((Dictionary<string, object>)none[section.Key]).Keys.OrderBy(k => k));
        Assert.True((bool)Part(oil, "burner")["lighting"]);
        Assert.Equal(">", ((Dictionary<string, object>)((List<object>)Part(oil, "burner")["steps"])[1])["mark"]);
    }

    [Fact]
    public void TakesWidenedNumbers()
    {
        var readings = Coal();
        var boiler = (Dictionary<string, object>)readings["boiler"];
        boiler["pressureBar"] = 12.5;
        boiler["workingBar"] = 14;

        var data = FiringData.FromReadings(readings);

        Assert.Equal(12.5f, data.Boiler.PressureBar);
        Assert.Equal(14f, data.Boiler.WorkingBar);
    }
}
