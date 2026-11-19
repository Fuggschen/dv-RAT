using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>
    /// One engine's fire, boiler and engine from the Realistic Firing mod. Units as in its API: Pct percent,
    /// Bar gauge bar, C °C, Kg, KgPerS, S seconds, Ppm. A level is "Ok", "Watch" (wants doing soon) or "Act" (wants doing now).
    /// A section the engine lacks is null; so is an optional value it lacks.
    /// </summary>
    public class FiringData
    {
        /// <summary>"Coal" or "Oil".</summary>
        public string Fuel { get; set; }

        public bool IsCoal => Fuel == "Coal";

        public FireReading Fire { get; set; } = new FireReading();

        /// <summary>Coal only.</summary>
        public GrateReading Grate { get; set; }

        /// <summary>Coal only.</summary>
        public CoalReading Coal { get; set; }

        /// <summary>With a mechanical stoker.</summary>
        public StokerReading Stoker { get; set; }

        /// <summary>Oil only.</summary>
        public BurnerReading Burner { get; set; }

        public BoilerReading Boiler { get; set; }

        /// <summary>The mod's own injectors; empty when the engine's own work.</summary>
        public List<InjectorReading> Injectors { get; set; } = new List<InjectorReading>();

        /// <summary>With the mod's injectors: water and steam wasted from their overflows.</summary>
        public OverflowReading Overflow { get; set; }

        public FeedPumpReading FeedPump { get; set; }

        /// <summary>While the mod works the cylinders.</summary>
        public EngineReading Engine { get; set; }

        public BoosterReading Booster { get; set; }

        /// <summary>With the mod's automatic fireman.</summary>
        public FiremanReading Fireman { get; set; }

        /// <summary>The next step at the foot of the mod's overlay.</summary>
        public string AdviceLevel { get; set; } = "Ok";

        public string AdviceText { get; set; } = "";

        /// <summary>Null when <paramref name="readings"/> is null or has no fuel, i.e. is not the API's snapshot. Values of the wrong type read as missing.</summary>
        public static FiringData FromReadings(IDictionary<string, object> readings)
        {
            if (!(Get(readings, "fuel") is string fuel)) return null;
            var data = new FiringData { Fuel = fuel };

            var fire = Section(readings, "fire");
            data.Fire = new FireReading
            {
                Lit = Flag(fire, "lit"),
                HeatPct = Num(fire, "heatPct") ?? 0f,
                Level = Text(fire, "level") ?? "Ok",
                DraftPct = Num(fire, "draftPct") ?? 0f,
                DraftLevel = Text(fire, "draftLevel") ?? "Ok",
                SmokePct = Num(fire, "smokePct") ?? 0f,
                TemperatureC = Num(fire, "temperatureC"),
            };

            if (Section(readings, "grate") is IDictionary<string, object> grate)
                data.Grate = new GrateReading
                {
                    CoalKg = Num(grate, "coalKg") ?? 0f,
                    AshpanPct = Num(grate, "ashpanPct") ?? 0f,
                    AshpanFull = Flag(grate, "ashpanFull"),
                    ClinkerKg = Num(grate, "clinkerKg") ?? 0f,
                    NeedsCleaning = Flag(grate, "needsCleaning"),
                    DoorPct = Num(grate, "doorPct"),
                    ThinZone = Text(grate, "thinZone") ?? "",
                    ZoneReferenceKg = Num(grate, "zoneReferenceKg") ?? 0f,
                    Zones = Items(grate, "zones").Select(zone => new GrateZoneReading
                    {
                        Name = Text(zone, "name") ?? "",
                        CoalKg = Num(zone, "coalKg") ?? 0f,
                        BurningPct = Num(zone, "burningPct") ?? 0f,
                        ClinkerKg = Num(zone, "clinkerKg") ?? 0f,
                    }).ToList(),
                };

            if (Section(readings, "coal") is IDictionary<string, object> coal)
                data.Coal = new CoalReading
                {
                    MoisturePct = Num(coal, "moisturePct") ?? 0f,
                    Condition = Text(coal, "condition") ?? "",
                    CoverOpen = Get(coal, "coverOpen") as bool?,
                };

            if (Section(readings, "stoker") is IDictionary<string, object> stoker)
                data.Stoker = new StokerReading
                {
                    Jammed = Flag(stoker, "jammed"),
                    SpeedPct = Num(stoker, "speedPct") ?? 0f,
                    TableKg = Num(stoker, "tableKg") ?? 0f,
                    TableLoaded = Flag(stoker, "tableLoaded"),
                    JetsBar = Num(stoker, "jetsBar") ?? 0f,
                };

            if (Section(readings, "burner") is IDictionary<string, object> burner)
                data.Burner = new BurnerReading
                {
                    FiringValvePct = Num(burner, "firingValvePct") ?? 0f,
                    StopPct = Num(burner, "stopPct"),
                    FeedKgPerS = Num(burner, "feedKgPerS") ?? 0f,
                    AtomizerPct = Num(burner, "atomizerPct") ?? 0f,
                    AtomizingPct = Num(burner, "atomizingPct") ?? 0f,
                    OilC = Num(burner, "oilC") ?? 0f,
                    HeaterPct = Num(burner, "heaterPct") ?? 0f,
                    BrickC = Num(burner, "brickC") ?? 0f,
                    OnShedSteam = Flag(burner, "onShedSteam"),
                    SteamLinePct = Num(burner, "steamLinePct") ?? 0f,
                    FlueSootPct = Num(burner, "flueSootPct") ?? 0f,
                    Lighting = Items(burner, "lighting").Select(step => new LightingStep
                    {
                        Title = Text(step, "title") ?? "",
                        How = Text(step, "how") ?? "",
                        Done = Flag(step, "done"),
                        Current = Flag(step, "current"),
                    }).ToList(),
                };

            if (Section(readings, "boiler") is IDictionary<string, object> boiler)
                data.Boiler = new BoilerReading
                {
                    PressureBar = Num(boiler, "pressureBar") ?? 0f,
                    WorkingBar = Num(boiler, "workingBar") ?? 0f,
                    PressurePct = Num(boiler, "pressurePct") ?? 0f,
                    PressureLevel = Text(boiler, "pressureLevel") ?? "Ok",
                    GlassPct = Num(boiler, "glassPct") ?? 0f,
                    GlassLevel = Text(boiler, "glassLevel") ?? "Ok",
                    WaterC = Num(boiler, "waterC"),
                    Heating = Get(boiler, "heating") as bool?,
                    SootLossPct = Num(boiler, "sootLossPct"),
                    ScaleLossPct = Num(boiler, "scaleLossPct"),
                    ImpuritiesPpm = Num(boiler, "impuritiesPpm"),
                    FoamPct = Num(boiler, "foamPct"),
                    PlugMelted = Get(Section(boiler, "plug"), "melted") as bool?,
                    PlugCoreC = Num(Section(boiler, "plug"), "coreC"),
                    AlarmSounding = Get(Section(boiler, "lowWaterAlarm"), "sounding") as bool?,
                };

            data.Injectors = Items(readings, "injectors").Select(injector => new InjectorReading
            {
                Name = Text(injector, "name") ?? "",
                State = Text(injector, "state") ?? "",
                WaterKgPerS = Num(injector, "waterKgPerS") ?? 0f,
                TenderShut = Flag(injector, "tenderShut"),
                SteamValve = Text(injector, "steamValve"),
                WaterValvePct = Num(injector, "waterValvePct"),
                LeverPct = Num(injector, "leverPct"),
            }).ToList();

            if (Section(readings, "overflow") is IDictionary<string, object> overflow)
                data.Overflow = new OverflowReading
                {
                    WaterKgPerS = Num(overflow, "waterKgPerS") ?? 0f,
                    SteamKgPerS = Num(overflow, "steamKgPerS") ?? 0f,
                };

            if (Section(readings, "feedPump") is IDictionary<string, object> pump)
                data.FeedPump = new FeedPumpReading
                {
                    State = Text(pump, "state") ?? "",
                    WaterKgPerS = Num(pump, "waterKgPerS") ?? 0f,
                    TemperatureC = Num(pump, "temperatureC") ?? 0f,
                };

            if (Section(readings, "engine") is IDictionary<string, object> engine)
                data.Engine = new EngineReading
                {
                    CutoffPct = Num(engine, "cutoffPct") ?? 0f,
                    SteamChestBar = Num(engine, "steamChestBar") ?? 0f,
                    MeanPressureBar = Num(engine, "meanPressureBar") ?? 0f,
                    SteamKgPerS = Num(engine, "steamKgPerS") ?? 0f,
                    CondensingPct = Num(engine, "condensingPct") ?? 0f,
                    Superheated = Flag(engine, "superheated"),
                    SuperheatC = Num(engine, "superheatC"),
                    SteamC = Num(engine, "steamC"),
                };

            if (Section(readings, "booster") is IDictionary<string, object> booster)
                data.Booster = new BoosterReading
                {
                    State = Text(booster, "state") ?? "",
                    WarmPct = Num(booster, "warmPct") ?? 0f,
                };

            if (Section(readings, "fireman") is IDictionary<string, object> fireman)
                data.Fireman = new FiremanReading
                {
                    Mode = Text(fireman, "mode") ?? "",
                    Call = Text(fireman, "call") ?? "None",
                    CallLeftS = Num(fireman, "callLeftS") ?? 0f,
                };

            var advice = Section(readings, "advice");
            data.AdviceLevel = Text(advice, "level") ?? "Ok";
            data.AdviceText = Text(advice, "text") ?? "";
            return data;
        }

        private static object Get(IDictionary<string, object> section, string key) =>
            section != null && section.TryGetValue(key, out object value) ? value : null;

        private static IDictionary<string, object> Section(IDictionary<string, object> section, string key) =>
            Get(section, key) as IDictionary<string, object>;

        // The API gives floats; other number types are taken too, so a widened type does not hide a value.
        private static float? Num(IDictionary<string, object> section, string key)
        {
            switch (Get(section, key))
            {
                case float f: return f;
                case double d: return (float)d;
                case int i: return i;
                default: return null;
            }
        }

        private static bool Flag(IDictionary<string, object> section, string key) => Get(section, key) is bool b && b;

        private static string Text(IDictionary<string, object> section, string key) => Get(section, key) as string;

        private static IEnumerable<IDictionary<string, object>> Items(IDictionary<string, object> section, string key) =>
            (Get(section, key) as System.Collections.IEnumerable)?.OfType<IDictionary<string, object>>()
            ?? Enumerable.Empty<IDictionary<string, object>>();
    }

    public class FireReading
    {
        public bool Lit { get; set; }

        /// <summary>Heat against the most the firebox gives; good band 40 to 90. Not capped: a cold start reads well past 100.</summary>
        public float HeatPct { get; set; }

        public string Level { get; set; } = "Ok";

        /// <summary>Air against what the fire wants, 100 the right mix. 0 while out, not capped.</summary>
        public float DraftPct { get; set; }

        public string DraftLevel { get; set; } = "Ok";

        /// <summary>0 clear to 100 black.</summary>
        public float SmokePct { get; set; }

        /// <summary>Coal only.</summary>
        public float? TemperatureC { get; set; }
    }

    public class GrateReading
    {
        public float CoalKg { get; set; }
        public float AshpanPct { get; set; }
        public bool AshpanFull { get; set; }
        public float ClinkerKg { get; set; }
        public bool NeedsCleaning { get; set; }

        /// <summary>Null on an engine whose fire door the mod can't read.</summary>
        public float? DoorPct { get; set; }

        /// <summary>Zone running short of coal, "" when none is.</summary>
        public string ThinZone { get; set; } = "";

        /// <summary>A zone's working bed.</summary>
        public float ZoneReferenceKg { get; set; }

        /// <summary>3 by 3 as seen from the cab: back row at the door first, each row left to right.</summary>
        public List<GrateZoneReading> Zones { get; set; } = new List<GrateZoneReading>();
    }

    public class GrateZoneReading
    {
        public string Name { get; set; }
        public float CoalKg { get; set; }

        /// <summary>Against the most a zone burns.</summary>
        public float BurningPct { get; set; }

        public float ClinkerKg { get; set; }
    }

    public class CoalReading
    {
        public float MoisturePct { get; set; }

        /// <summary>"Dry", "Damp" or "Wet".</summary>
        public string Condition { get; set; }

        /// <summary>Only on an engine whose coal has a cover.</summary>
        public bool? CoverOpen { get; set; }
    }

    public class StokerReading
    {
        public bool Jammed { get; set; }
        public float SpeedPct { get; set; }
        public float TableKg { get; set; }

        /// <summary>Coal piling on the table that wants raking.</summary>
        public bool TableLoaded { get; set; }

        public float JetsBar { get; set; }
    }

    public class BurnerReading
    {
        public float FiringValvePct { get; set; }

        /// <summary>Only while a stop is set.</summary>
        public float? StopPct { get; set; }

        public float FeedKgPerS { get; set; }
        public float AtomizerPct { get; set; }

        /// <summary>Atomizer steam against what the oil needs, under 100 too little.</summary>
        public float AtomizingPct { get; set; }

        public float OilC { get; set; }
        public float HeaterPct { get; set; }
        public float BrickC { get; set; }
        public bool OnShedSteam { get; set; }
        public float SteamLinePct { get; set; }
        public float FlueSootPct { get; set; }

        /// <summary>While the fire is out, the steps to light it in order; empty while lit.</summary>
        public List<LightingStep> Lighting { get; set; } = new List<LightingStep>();
    }

    public class LightingStep
    {
        public string Title { get; set; }
        public string How { get; set; }
        public bool Done { get; set; }
        public bool Current { get; set; }
    }

    public class BoilerReading
    {
        public float PressureBar { get; set; }

        /// <summary>Where the safety valves lift.</summary>
        public float WorkingBar { get; set; }

        /// <summary>Good 85 to 100. Not capped: under 0 below atmospheric, over 100 past the valves.</summary>
        public float PressurePct { get; set; }

        public string PressureLevel { get; set; } = "Ok";

        /// <summary>Water as the cab's gauge glass shows it, good 40 to 75.</summary>
        public float GlassPct { get; set; }

        public string GlassLevel { get; set; } = "Ok";

        // The rest only with the mod's boiler water.
        public float? WaterC { get; set; }

        /// <summary>Raising steam from cold, the water below boiling.</summary>
        public bool? Heating { get; set; }

        public float? SootLossPct { get; set; }
        public float? ScaleLossPct { get; set; }

        /// <summary>Only while the mod's impurities setting is on, like <see cref="FoamPct"/>.</summary>
        public float? ImpuritiesPpm { get; set; }

        public float? FoamPct { get; set; }

        /// <summary>Null without a fusible plug, like <see cref="PlugCoreC"/>.</summary>
        public bool? PlugMelted { get; set; }

        public float? PlugCoreC { get; set; }

        /// <summary>Null without a low water alarm.</summary>
        public bool? AlarmSounding { get; set; }
    }

    public class InjectorReading
    {
        /// <summary>"right" or "left" on an engine with more than one, else "".</summary>
        public string Name { get; set; }

        /// <summary>"Off", "Priming", "Working", "Hot", "LowPressure", "NoWater" or "Broken".</summary>
        public string State { get; set; }

        public float WaterKgPerS { get; set; }

        /// <summary>The tender's valve to it is shut.</summary>
        public bool TenderShut { get; set; }

        /// <summary>"Off", "Prime" or "Feed"; null on an injector worked by one lever, which has <see cref="LeverPct"/> instead.</summary>
        public string SteamValve { get; set; }

        public float? WaterValvePct { get; set; }
        public float? LeverPct { get; set; }
    }

    public class OverflowReading
    {
        public float WaterKgPerS { get; set; }
        public float SteamKgPerS { get; set; }
    }

    public class FeedPumpReading
    {
        /// <summary>"Off", "Feeding", "Choked", "Racing", "LostSuction" or "Tripped".</summary>
        public string State { get; set; }

        public float WaterKgPerS { get; set; }
        public float TemperatureC { get; set; }
    }

    public class EngineReading
    {
        public float CutoffPct { get; set; }
        public float SteamChestBar { get; set; }

        /// <summary>Mean effective pressure.</summary>
        public float MeanPressureBar { get; set; }

        public float SteamKgPerS { get; set; }

        /// <summary>Share of the steam condensing in the cylinders.</summary>
        public float CondensingPct { get; set; }

        public bool Superheated { get; set; }

        /// <summary>Only while superheated, like <see cref="SteamC"/>.</summary>
        public float? SuperheatC { get; set; }

        public float? SteamC { get; set; }
    }

    public class BoosterReading
    {
        /// <summary>"CutOut", "Idling", "CocksOpen" or "Working".</summary>
        public string State { get; set; }

        public float WarmPct { get; set; }
    }

    public class FiremanReading
    {
        /// <summary>"Off", "KeepAlive", "Shunting" or "Road".</summary>
        public string Mode { get; set; }

        /// <summary>"None", "HeavyAhead" or "EasingOff".</summary>
        public string Call { get; set; }

        /// <summary>Until the call lapses.</summary>
        public float CallLeftS { get; set; }
    }
}
