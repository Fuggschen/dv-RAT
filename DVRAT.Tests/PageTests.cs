using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DVRAT;
using Xunit;

public class TemplateTests
{
    private static readonly Dictionary<string, object> Root = new Dictionary<string, object>
    {
        ["train"] = new Dictionary<string, object>
        {
            ["speedKmh"] = 42.6f,
            ["onTrain"] = true,
            ["name"] = "",
            ["wagons"] = new List<object> { "a", "b" },
            ["empty"] = new List<object>(),
            ["zero"] = 0,
        },
        ["page"] = new Dictionary<string, object> { ["tab"] = "active" },
    };

    [Fact]
    public void PlacesACarOnItsTrack()
    {
        // Centred on its bogies, cut off at the track end, and counted from the other end when flipped.
        Assert.Equal((10f, 24f), TrackCar.Extent(13f, 21f, 14f, 100f, flip: false));
        Assert.Equal((76f, 90f), TrackCar.Extent(13f, 21f, 14f, 100f, flip: true));
        Assert.Equal((95f, 100f), TrackCar.Extent(102f, null, 14f, 100f, flip: false));
        Assert.Equal((0f, 7f), TrackCar.Extent(null, 0f, 14f, 100f, flip: false));
    }

    [Fact]
    public void CountsASettingUpAndDown()
    {
        var root = new Dictionary<string, object> { ["settings"] = new Dictionary<string, object> { ["brightness"] = "100" } };

        Assert.Equal("90", Template.Format("{settings.brightness - 10}", root));
        Assert.True(Template.Evaluate("settings.brightness>10", root));
        Assert.False(Template.Evaluate("settings.brightness<100", root));
    }

    [Fact]
    public void FormatsValuesWithAndWithoutFormat()
    {
        Assert.Equal("43 km/h", Template.Format("{train.speedKmh:F0} km/h", Root));
        Assert.Equal("42.6", Template.Format("{train.speedKmh}", Root));
    }

    [Fact]
    public void UnknownPathIsVisibleAndReported()
    {
        var missing = new List<string>();

        string text = Template.Format("v {train.sped}", Root, missing);

        Assert.Equal("v {?train.sped}", text);
        Assert.Equal(new[] { "train.sped" }, missing);
    }

    [Fact]
    public void ListsAnswerCount()
    {
        Assert.Equal("2", Template.Format("{train.wagons.count}", Root));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("HB-FH-12", true)]
    [InlineData("HB-FH-12,HB-SU-03", false)]
    public void PageVariablesCountTheirValues(string selected, bool exactlyOne)
    {
        var root = new Dictionary<string, object> { ["page"] = new Dictionary<string, object> { ["selected"] = selected } };

        Assert.Equal(exactlyOne, Template.Evaluate("page.selected.count=1", root));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("train.onTrain", true)]
    [InlineData("!train.onTrain", false)]
    [InlineData("train.name", false)]
    [InlineData("train.zero", false)]
    [InlineData("train.empty", false)]
    [InlineData("train.wagons", true)]
    [InlineData("page.tab=active", true)]
    [InlineData("page.tab!=active", false)]
    [InlineData("page.tab=coupled", false)]
    [InlineData("train.onTrain && page.tab=active", true)]
    [InlineData("train.onTrain && !train.wagons", false)]
    [InlineData("train.zero || page.tab=active", true)]
    [InlineData("train.zero || train.name", false)]
    [InlineData("train.zero && train.onTrain || train.onTrain", true)]
    [InlineData("train.speedKmh > 40", true)]
    [InlineData("train.speedKmh >= 42.6", true)]
    [InlineData("train.speedKmh < 40", false)]
    [InlineData("train.speedKmh / 2 <= train.wagons.count * 20", true)]
    [InlineData("train.wagons.count * 2 = 4", true)]
    public void EvaluatesConditions(string condition, bool expected)
    {
        Assert.Equal(expected, Template.Evaluate(condition, Root));
    }

    [Theory]
    [InlineData("{train.speedKmh * 2:F0}", "85")]
    [InlineData("{(train.wagons.count + 1) * -2}", "-6")]
    [InlineData("{100 - train.speedKmh:F1} left", "57.4 left")]
    [InlineData("{train.speedKmh / train.zero}", "--")]
    [InlineData("{7 % 3}", "1")]
    public void ComputesArithmetic(string template, string expected)
    {
        Assert.Equal(expected, Template.Format(template, Root));
    }

    [Fact]
    public void UnknownPathInArithmeticIsVisibleAndReported()
    {
        var missing = new List<string>();

        Assert.Equal("{?train.nope + 1}", Template.Format("{train.nope + 1}", Root, missing));
        Assert.False(Template.Evaluate("train.nope > 1", Root, missing));
        Assert.Equal(new[] { "train.nope", "train.nope" }, missing);
    }

    [Theory]
    [InlineData("page.tab in active,coupled", true)]
    [InlineData("page.tab !in active,coupled", false)]
    [InlineData("train.wagons in page.hide", true)]
    [InlineData("train.wagons !in page.none", true)]
    [InlineData("page.tab in page.none", false)]
    public void InChecksForCommonValues(string condition, bool expected)
    {
        var root = new Dictionary<string, object>(Root)
        {
            ["page"] = new Dictionary<string, object> { ["tab"] = "active", ["hide"] = "x,b", ["none"] = "" },
        };

        Assert.Equal(expected, Template.Evaluate(condition, root));
    }

    [Fact]
    public void UnknownPathInConditionIsFalseAndReported()
    {
        var missing = new List<string>();

        Assert.False(Template.Evaluate("train.nope", Root, missing));
        Assert.True(Template.Evaluate("!train.nope", Root, missing));
        Assert.Equal(new[] { "train.nope", "train.nope" }, missing);
    }
}

public class PageDefinitionTests
{
    [Fact]
    public void ReportsUnknownRowTypeAndWidget()
    {
        var problems = new List<string>();

        PageDefinitions.Parse(@"{ ""id"": ""x"", ""rows"": [ { ""type"": ""txt"" }, { ""type"": ""widget"", ""name"": ""clock"" } ] }",
            "x.json", PageDefinitions.WidgetNames, problems);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("txt"));
        Assert.Contains(problems, p => p.Contains("clock"));
    }

    [Fact]
    public void BrokenJsonAndMissingIdGiveNoPage()
    {
        var problems = new List<string>();

        Assert.Null(PageDefinitions.Parse("{ nope", "a.json", PageDefinitions.WidgetNames, problems));
        Assert.Null(PageDefinitions.Parse(@"{ ""title"": ""x"" }", "b.json", PageDefinitions.WidgetNames, problems));
        Assert.Equal(2, problems.Count);
    }
}

/// <summary>Catches typos in the shipped pages/ that would only show at runtime.</summary>
public class ShippedPageTests
{
    private static Dictionary<string, object> SampleBindings(bool withData, Dictionary<string, string> vars)
    {
        if (!withData)
            return PageBindings.Build(null, null, null, null, vars, id => id);

        var loco = new LocoInfo { CarId = "L-1", CarType = "LocoShunter", Mass = 45, HasTraction = true, TmTemperature = 80, TmOverheatTemperature = 120, Fee = 250,
            Damage = new DamageInfo
            {
                Parts = { new DamagePart { Name = "Body", Health = 0.9f }, new DamagePart { Name = "Electrical powertrain", Health = 0.4f } },
                WorkingMotors = 3, TotalMotors = 4,
                Fuses = { new FuseState { Id = "electronics.MAIN_FUSE", On = true } },
            },
            Ports ={ new SimPort { Id = "engine.RPM", Type = "RPM", Value = 800 } } };
        var wagon = new CarInfo { CarId = "W-1", CarType = "FlatbedStakes", Mass = 30, LoadedCargo = "Logs", CargoAmount = 20 };
        var train = new TrainData
        {
            Cars = new List<CarInfo> { loco, wagon },
            Locomotive = loco,
            WagonCount = 1,
            SpeedKmh = 40,
            Braking = new BrakingInfo { SpeedMs = 11, CurrentBrakingDistance = 400, EmergencyBrakingDistance = 250 },
            SolarGadgets = true,
            Electric = new ElectricInfo
            {
                CarId = "L-1", Source = "Catenary for CCL", LineVoltage = 1480, NominalVoltage = 1500, LineCurrent = 900,
                PantographsReported = true, MotorTemperature = 60, WorkingMotors = 4,
                Battery = new TractionBattery { Active = true, Wanted = true, Voltage = 410, Current = 300, Charge = 0.6f },
                EnergyUnits = 20, KwhPerUnit = 1.5f,
                Pantographs = { new PantographState { Name = "pantograph", Raised = 1, InContact = true, WireVoltage = 1480 } },
            },
            Power =
            {
                new CarPower
                {
                    CarId = "L-1", Supply = 0, Demand = 3, Charge = 60, Capacity = 100, Net = -3, Stored = 60, Room = 40,
                    Batteries = { new PowerBattery { Name = "Loco battery", Soldered = true, Charge = 60, Capacity = 100 } },
                    Consumers = { new PowerConsumer { Name = "Loco lights", Draw = 3 } },
                },
            },
        };
        var route = new RouteData
        {
            CurrentTrackId = "HB-1-S",
            CurrentTrackName = "HB-1-S",
            Direction = "forward",
            NextStationName = "SM",
            DistanceToNextStation = 3000,
            UpcomingEvents = new List<TrackEvent>
            {
                new TrackEvent { EventType = TrackEventType.Station, StationId = "SM", DistanceMeters = 3000 },
                new TrackEvent { EventType = TrackEventType.Junction, Name = "J1", DistanceMeters = 200 },
            },
        };
        var job = new JobInfo { JobId = "J-1", JobTypeDisplay = "Haul", JobTypeShort = "FH", Licenses = { "FH", "HZ1" }, DestinationStation = "SM", OriginDisplay = "HB", DestinationDisplay = "SM", IsCoupled = true };
        var jobs = new JobData { AcceptedJobs = new List<JobInfo> { job }, CoupledJobs = new List<JobInfo> { job }, ActiveJobs = new List<JobInfo> { job } };
        var world = new WorldData
        {
            GameTime = new DateTime(2026, 5, 1, 14, 30, 0),
            Money = 12500,
            Licenses = { new License { Name = "Freight Haul", Kind = "job" } },
            Fees = { new Fee { Id = "HB-FH-12", Kind = "job", Price = 250, Payable = true } },
            OwnedCars = { new OwnedCar { Id = "L-004", Type = "LocoDE2", ServicePrice = 120, Track = "HB-1S", Distance = 400 } },
            NearestStationId = "HB",
            AtStation = true,
            JobsKnown = true,
            StationJobs = { job },
            StationTracks = { new TrackSpace { Id = "HB-1S", Length = 300, Free = 120 } },
            SwitchesKnown = true,
            StationSwitches = { new SwitchState { Id = "S-0228-HB", Damage = 1f, Locked = true }, new SwitchState { Id = "S-0229-HB" } },
            TrackDamageKnown = true,
            StationTrackDamage = { new DamagedSection { Track = "HB-1S", From = 40, To = 60, DamagePercent = 12, LimitKmh = 50 } },
            Integrations =
            {
                new IntegrationStatus { Name = "Solar Gadgets", State = IntegrationState.Active },
                new IntegrationStatus { Name = "DVSeasons", State = IntegrationState.Failed },
            },
            Stations = { new StationEntry { Id = "HB", Distance = 50 }, new StationEntry { Id = "SM", Distance = 4000 } },
            Forecast =
            {
                new ForecastSlot { Start = new DateTime(2026, 5, 1, 12, 0, 0), Hours = 3, Icon = "Clear_Day", TemperatureC = 14.6f },
                new ForecastSlot { Start = new DateTime(2026, 5, 1, 15, 0, 0), Hours = 3, Icon = "LightRain_Day", RainPct = 6, TemperatureC = 11f },
            },
            SeasonsInstalled = true,
            Seasons = new SeasonsData
            {
                Season = "Spring", NextSeason = "Summer", Transition = 0.4f, Snow = 0f, TemperatureC = 13.2f,
                Blizzard = "No blizzard forecast",
            },
        };
        return PageBindings.Build(train, route, jobs, world, vars, id => "Station " + id);
    }

    public static IEnumerable<object[]> PageFiles() =>
        Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pages"), "*.json")
            .Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void PagesAreShipped()
    {
        var names = PageFiles().Select(f => (string)f[0]).ToList();

        Assert.Contains("home.json", names);
        Assert.True(names.Count >= 7, string.Join(", ", names));
    }

    [Fact]
    public void EveryPageHangsInTheTreeBelowHome()
    {
        var pages = PageFiles().Select(f => (string)f[0]).Select(name => PageDefinitions.Parse(
            File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pages", name)), name,
            PageDefinitions.WidgetNames, new List<string>())).ToList();
        // JSON pages with no parent sit on home, as JsonPage does it.
        var parents = pages.Where(p => p.Id != "home").ToDictionary(p => p.Id, p => p.Parent ?? "home");
        var generated = new[] { ContentIds.Info, ContentIds.Stations, ContentIds.Cargo, ContentIds.RollingStock };

        foreach (var page in pages)
            Assert.False(page.Hidden, $"{page.Id} is hidden; every shipped page is listed on a parent");
        foreach (string id in parents.Keys)
        {
            var seen = new HashSet<string> { id };
            string current = parents[id];
            while (current != "home" && !generated.Contains(current))
            {
                Assert.True(parents.ContainsKey(current), $"{id}: parent '{current}' does not exist");
                Assert.True(seen.Add(current), $"{id}: parents loop through '{current}'");
                current = parents[current];
            }
        }
    }

    [Theory]
    [MemberData(nameof(PageFiles))]
    public void PageParsesAndUsesOnlyKnownBindings(string fileName)
    {
        string json = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pages", fileName));
        var problems = new List<string>();
        var page = PageDefinitions.Parse(json, fileName, PageDefinitions.WidgetNames, problems);
        Assert.NotNull(page);
        Assert.Empty(problems);

        var missing = new List<string>();
        foreach (bool withData in new[] { true, false })
        {
            var root = SampleBindings(withData, page.Vars);
            foreach (var row in page.Rows)
                CheckRow(row, root, missing, withData);
            foreach (var key in page.SoftKeys)
            {
                Template.Format(key.Label, root, missing);
                Template.Format(key.Link, root, missing);
                Template.Evaluate(key.When, root, missing);
                Template.Evaluate(key.EnabledWhen, root, missing);
                Template.Format(key.PlanJobs, root, missing);
            }
        }
        Assert.Empty(missing.Distinct());
    }

    private static void CheckRow(RowDefinition row, Dictionary<string, object> root, List<string> missing, bool withData)
    {
        Template.Evaluate(row.When, root, missing);
        // Filled per item, checked below.
        if (row.Type != "checklist")
            Template.Format(row.Text, root, missing);
        if (row.Type != "checklist" && row.Type != "list")
            Template.Format(row.Value, root, missing);
        foreach (var pair in row.Items ?? new List<List<string>>())
            Template.Format(pair[1], root, missing);
        foreach (var button in row.Buttons ?? new List<SoftKeyDefinition>())
        {
            Template.Format(button.Label, root, missing);
            Template.Evaluate(button.When, root, missing);
            Template.Evaluate(button.EnabledWhen, root, missing);
            Template.Evaluate(button.SelectedWhen, root, missing);
        }
        if (row.Type == "bar")
        {
            Template.Evaluate(row.WarnWhen, root, missing);
            foreach (string number in new[] { row.Value, row.Min, row.Max }.Where(n => n != null))
                Template.TryNumber(number, root, missing, out _);
        }

        if (row.Type == "checklist")
        {
            string options = Template.Format(row.Source, root, missing);
            Assert.True(Template.TryResolve(root, options, out object optionList), $"checklist source '{options}' not found");
            if (withData && ((IList)optionList).Count > 0)
            {
                var optionRoot = PageBindings.WithItem(root, ((IList)optionList)[0]);
                Template.Format(row.Text, optionRoot, missing);
                Template.Format(row.Value, optionRoot, missing);
                Template.Evaluate(row.EnabledWhen, optionRoot, missing);
            }
        }
        if (row.Type != "list") return;

        string source = Template.Format(row.Source, root, missing);
        Assert.True(Template.TryResolve(root, source, out object value), $"list source '{source}' not found");
        var items = (IList)value;
        // Item paths need an item, and the sample data has one per list.
        if (!withData) return;
        Assert.True(items.Count > 0, $"sample data has no items for '{source}'");

        var itemRoot = PageBindings.WithItem(root, items[0]);
        Template.Evaluate(row.Filter, itemRoot, missing);
        Template.Format(row.Value, itemRoot, missing);
        if (row.Click != null)
        {
            Template.Format(row.Click.Link, itemRoot, missing);
            foreach (string setValue in (row.Click.Set ?? new Dictionary<string, string>()).Values)
                Template.Format(setValue, itemRoot, missing);
        }
        foreach (var column in row.Columns)
        {
            Template.Format(column.Text, itemRoot, missing);
            Template.Evaluate(column.When, itemRoot, missing);
            Template.Evaluate(column.WarnWhen, itemRoot, missing);
        }
    }
}

public class PageBindingTests
{
    [Fact]
    public void CarsOfOneJobToTheSameTrackShareARow()
    {
        CarInfo Car(string id, string job, string next, float pipe) =>
            new CarInfo { CarId = id, CarType = "Flatbed", JobId = job, NextDestination = next, BrakePipePressure = pipe, Mass = 20 };
        var cars = new List<CarInfo>
        {
            new CarInfo { CarId = "L-1", CarType = "DE2", IsLocomotive = true, Mass = 38 },
            Car("F1", "J1", "SM-C1L", 4.8f), Car("F2", "J1", "SM-C1L", 4.6f), Car("F3", "J2", "SM-C1L", 5f), Car("F4", null, null, 5f),
        };

        var groups = PageBindings.CarRows(cars, true).Cast<Dictionary<string, object>>().ToList();

        Assert.Equal(new[] { "1", "2-3", "4", "5" }, groups.Select(g => (string)g["pos"]));
        Assert.Equal("2x Flatbed", groups[1]["name"]);
        Assert.Equal(4.6f, groups[1]["pipeBar"]);
        Assert.Equal(40f, groups[1]["massT"]);
        Assert.Equal(5, PageBindings.CarRows(cars, false).Count);
    }

    [Fact]
    public void DelayComparesTheArrivalWithTheTimeLeft()
    {
        var route = new RouteData
        {
            SpeedSections = { new RouteSection { Start = 0, End = 5000, Value = 36 } },
            UpcomingEvents = { new TrackEvent { EventType = TrackEventType.Station, StationId = "SM", DistanceMeters = 3000 } },
        };
        var job = new JobInfo { DestinationStation = "SM", TimeLeft = TimeSpan.FromSeconds(200) };

        // 3000 m at 10 m/s is 300 s, 100 s more than the job has left.
        Assert.Equal(100, PageBindings.Delay(route, job).Value, 3);
        Assert.Equal("+2 min", PageBindings.DelayText(100));
        Assert.Equal("on time", PageBindings.DelayText(20));
        Assert.Equal("-5 min", PageBindings.DelayText(-300));
        Assert.Null(PageBindings.Delay(route, new JobInfo { DestinationStation = "HB", TimeLeft = TimeSpan.FromSeconds(200) }));
    }


    [Fact]
    public void SwitchDamageShowsAsTheModListsIt()
    {
        var world = new WorldData
        {
            SwitchesKnown = true,
            StationSwitches =
            {
                new SwitchState { Id = "S-1-HB", Damage = 1f, Locked = true },
                new SwitchState { Id = "S-2-HB", Damage = 0.002f },
                new SwitchState { Id = "S-3-HB", Repairing = true, Locked = true },
            },
        };
        var root = PageBindings.Build(null, null, null, world, new Dictionary<string, string>(), id => id);

        Assert.Equal("2", Template.Format("{station.damagedSwitches}", root));
        var switches = ((List<object>)((Dictionary<string, object>)root["station"])["switches"]).Cast<Dictionary<string, object>>().ToList();
        Assert.Equal(new object[] { 100, 1, 0 }, switches.Select(s => s["damagePct"]));
        Assert.Equal(new object[] { "locked", "", "under repair" }, switches.Select(s => s["state"]));
    }

    [Fact]
    public void ClosedTrackShowsAsZeroNotAsUnknown()
    {
        var route = new RouteData
        {
            CurrentTrackId = "T1",
            SpeedSections = { new RouteSection { Start = 0, End = 20, Value = 0 }, new RouteSection { Start = 20, End = 500, Value = 80 } },
            Restrictions = { new SpeedRestriction { From = -20, To = 20, LimitKmh = 0, DamagePercent = 41 } },
        };
        var root = PageBindings.Build(null, route, null, null, new Dictionary<string, string>(), id => id);

        Assert.Equal("0 km/h 41 %", Template.Format("{route.limit} {route.trackDamagePct} %", root));
        Assert.True(Template.Evaluate("route.trackClosed", root));
    }

    [Fact]
    public void TrackDamageListsItsAdvisedSpeed()
    {
        var world = new WorldData
        {
            TrackDamageKnown = true,
            StationTrackDamage =
            {
                new DamagedSection { Track = "HB-1S", From = 40, To = 60, DamagePercent = 36, LimitKmh = 0 },
                new DamagedSection { Track = "S-1-HB", From = 0, To = 20, DamagePercent = 12, LimitKmh = 50 },
                new DamagedSection { Track = "HB-2S", From = 20, To = 33.4f, DamagePercent = 3 },
            },
        };
        var root = PageBindings.Build(null, null, null, world, new Dictionary<string, string>(), id => id);

        var damage = ((List<object>)((Dictionary<string, object>)root["station"])["trackDamage"]).Cast<Dictionary<string, object>>().ToList();
        Assert.Equal(new object[] { "closed", "50 km/h", "--" }, damage.Select(d => d["limit"]));
        Assert.Equal("20-33 m", damage[2]["range"]);
    }

    [Fact]
    public void JobCarsAreOneFlatListWithTheirNextLeg()
    {
        var car = new JobCar { Id = "C1", Track = "HB-1S", Length = 14f };
        car.Legs.Add(new JobLeg { Kind = JobLegKind.Load, FromTrack = "HB-1S", ToTrack = "HB-7L" });
        car.Legs.Add(new JobLeg { Kind = JobLegKind.Transport, FromTrack = "HB-7L", ToTrack = "SM-2O" });
        var job = new JobInfo { JobId = "J1", Cars = { car }, TimeLimit = TimeSpan.FromMinutes(30), TimeLeft = TimeSpan.FromSeconds(-90) };
        var root = PageBindings.Build(null, null, new JobData { AcceptedJobs = { job } }, null, new Dictionary<string, string>(), id => id);

        Assert.Equal("J1 C1 load HB-7L", Template.Format("{item.jobId} {item.id} {item.action} {item.to}",
            PageBindings.WithItem(root, ((IList)((Dictionary<string, object>)root["jobs"])["cars"])[0])));
        Assert.Equal("1", Template.Format("{jobs.cars.count}", root));
        var jobRoot = PageBindings.WithItem(root, ((IList)((Dictionary<string, object>)root["jobs"])["accepted"])[0]);
        Assert.Equal("-1 min 30 s true", Template.Format("{item.timeLeft} {item.late}", jobRoot));
    }

    [Fact]
    public void PortsCanBeReadByTheirDottedId()
    {
        var loco = new LocoInfo { Ports = { new SimPort { Id = "engine.RPM", Value = 800 }, new SimPort { Id = "boiler.PRESSURE", Value = 12.5f } } };
        var root = PageBindings.Build(new TrainData { Locomotive = loco }, null, null, null, new Dictionary<string, string>(), id => id);

        Assert.Equal("800 12.5 false", Template.Format("{loco.port.engine.RPM} {loco.port.boiler.PRESSURE} {loco.coal.has}", root));
    }
}

public class ListQueryTests
{
    private static readonly List<object> Jobs = new List<object>
    {
        new Dictionary<string, object> { ["id"] = "B", ["pay"] = 900f, ["coupled"] = true },
        new Dictionary<string, object> { ["id"] = "a", ["pay"] = 1500f, ["coupled"] = false },
        new Dictionary<string, object> { ["id"] = "C", ["pay"] = 200f, ["coupled"] = true },
    };

    private static string Ids(RowDefinition row) =>
        string.Join(" ", ListQuery.Apply(Jobs, new Dictionary<string, object>(), row).Select(j => ((Dictionary<string, object>)j)["id"]));

    [Fact]
    public void FiltersSortsAndLimits()
    {
        Assert.Equal("B C", Ids(new RowDefinition { Filter = "item.coupled" }));
        Assert.Equal("C B a", Ids(new RowDefinition { Sort = "item.pay" }));
        Assert.Equal("a B", Ids(new RowDefinition { Sort = "item.pay", Descending = "true", Limit = 2 }));
        Assert.Equal("a B C", Ids(new RowDefinition { Sort = "item.id" }));
        Assert.Equal("B C", Ids(new RowDefinition { Filter = "item.pay < 1000", Sort = "0 - item.pay" }));
    }

    [Fact]
    public void SortAndDirectionCanComeFromPageVariables()
    {
        var root = new Dictionary<string, object>
        {
            ["page"] = new Dictionary<string, object> { ["sort"] = "item.pay", ["desc"] = "true" },
        };
        var row = new RowDefinition { Sort = "{page.sort}", Descending = "{page.desc}" };

        Assert.Equal("a B C", string.Join(" ", ListQuery.Apply(Jobs, root, row).Select(j => ((Dictionary<string, object>)j)["id"])));
    }

    [Theory]
    [InlineData(12.4, "$12/min")]
    [InlineData(0.0, "--")]
    public void ZeroSectionOfANumberFormatShowsAPlaceholder(double perMinute, string expected)
    {
        var root = new Dictionary<string, object> { ["x"] = perMinute };

        Assert.Equal(expected, Template.Format("{x:$0/min;-0;--}", root));
    }

    [Fact]
    public void ListKeysOnOtherRowsAreReported()
    {
        var problems = new List<string>();

        PageDefinitions.Parse(@"{ ""id"": ""x"", ""rows"": [ { ""type"": ""text"", ""sort"": ""item.id"" } ] }", "x.json",
            PageDefinitions.WidgetNames, problems);

        Assert.Single(problems);
    }
}
