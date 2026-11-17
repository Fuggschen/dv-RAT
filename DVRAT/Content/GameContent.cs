using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DV;
using DV.Localization;
using DV.Shops;
using DV.ThingTypes;
using DV.Utils;
using System.Text.RegularExpressions;
using LocoSim.Definitions;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Generates one info page per station, cargo type and car type from the loaded game.</summary>
    public static class GameContent
    {
        public static void AddTo(ContentStore store, Action<string> warn)
        {
            store.Add(Menu(ContentIds.Info, "Reference", "world", 60));
            Run(() => AddStations(store), "stations", warn);
            Run(() => AddCargo(store), "cargo", warn);
            Run(() => AddRollingStock(store), "rolling stock", warn);
        }

        private static void Run(Action generate, string what, Action<string> warn)
        {
            try
            {
                using (Timing.Measure($"Generating the {what} pages"))
                    generate();
            }
            catch (Exception ex)
            {
                warn($"Could not generate {what} pages: {ex.Message}");
            }
        }

        private static InfoPageData Menu(string id, string title, string parentId, int order) =>
            new InfoPageData(id, title, parentId, order) { IsMenu = true };

        #region Stations

        private static void AddStations(ContentStore store)
        {
            store.Add(Menu(ContentIds.Stations, "Stations", ContentIds.Info, 10));

            foreach (var station in StationController.allStations.Where(s => s != null && s.stationInfo != null))
            {
                var info = station.stationInfo;
                var page = new InfoPageData(ContentIds.Station(info.YardID), info.Name, ContentIds.Stations);
                page.Blocks.Add(InfoBlock.Field("Yard ID", info.YardID));
                page.Blocks.Add(InfoBlock.Field("Type", info.Type));

                var rules = station.proceduralJobsRuleset;
                if (rules != null)
                {
                    page.Blocks.Add(InfoBlock.Field("Job types", JobTypes(rules)));
                    page.Blocks.Add(InfoBlock.Field("Cars per job", $"{rules.minCarsPerJob} to {rules.maxCarsPerJob}"));
                    AddCargoFlows(page, "Ships out", rules.outputCargoGroups, "to");
                    AddCargoFlows(page, "Receives", rules.inputCargoGroups, "from");
                }
                store.Add(page);
            }
        }

        private static string JobTypes(StationProceduralJobsRuleset rules)
        {
            var types = new List<string>();
            if (rules.haulStartingJobSupported) types.Add("freight haul");
            if (rules.loadStartingJobSupported) types.Add("shunting load");
            if (rules.unloadStartingJobSupported) types.Add("shunting unload");
            if (rules.emptyHaulStartingJobSupported) types.Add("logistic haul");
            return types.Count > 0 ? string.Join(", ", types) : "none";
        }

        private static void AddCargoFlows(InfoPageData page, string heading, List<CargoGroup> groups, string direction)
        {
            if (groups == null || groups.Count == 0) return;

            page.Blocks.Add(InfoBlock.Heading(heading));
            foreach (var group in groups)
            {
                page.Blocks.Add(InfoBlock.Links("Cargo", group.cargoTypes.Select(c => ContentIds.CargoType(CargoId(c)))));
                var stations = group.stations.Where(s => s?.stationInfo != null)
                    .Select(s => ContentIds.Station(s.stationInfo.YardID));
                page.Blocks.Add(InfoBlock.Links(direction, stations).WithPrevious());
            }
        }

        #endregion

        #region Cargo

        private static void AddCargo(ContentStore store)
        {
            store.Add(Menu(ContentIds.Cargo, "Cargo", ContentIds.Info, 20));
            var types = Globals.G.Types;

            var shippedFrom = new Dictionary<string, List<string>>();
            var deliveredTo = new Dictionary<string, List<string>>();
            foreach (var station in StationController.allStations.Where(s => s?.stationInfo != null && s.proceduralJobsRuleset != null))
            {
                string stationId = ContentIds.Station(station.stationInfo.YardID);
                foreach (var cargo in station.proceduralJobsRuleset.outputCargoGroups.SelectMany(g => g.cargoTypes))
                    Append(shippedFrom, CargoId(cargo), stationId);
                foreach (var cargo in station.proceduralJobsRuleset.inputCargoGroups.SelectMany(g => g.cargoTypes))
                    Append(deliveredTo, CargoId(cargo), stationId);
            }

            foreach (var cargo in types.cargos.Where(c => c != null))
            {
                var page = new InfoPageData(ContentIds.CargoType(cargo.id), Localized(cargo.localizationKeyFull, cargo.id), ContentIds.Cargo);
                page.Blocks.Add(InfoBlock.Field("Mass per unit", $"{cargo.massPerUnit / 1000f:0.##} t"));

                if (types.CargoToLoadableCarTypes.TryGetValue(cargo, out var carTypes) && carTypes.Count > 0)
                    page.Blocks.Add(InfoBlock.Links("Loaded into", carTypes.Select(t => ContentIds.CarType(t.id))));
                if (shippedFrom.TryGetValue(cargo.id, out var from))
                    page.Blocks.Add(InfoBlock.Links("Shipped from", from));
                if (deliveredTo.TryGetValue(cargo.id, out var to))
                    page.Blocks.Add(InfoBlock.Links("Delivered to", to));
                store.Add(page);
            }
        }

        private static string CargoId(CargoType cargo) =>
            Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ? v2.id : cargo.ToString();

        #endregion

        #region Rolling stock

        private static void AddRollingStock(ContentStore store)
        {
            store.Add(Menu(ContentIds.RollingStock, "Rolling stock", ContentIds.Info, 30));
            var types = Globals.G.Types;
            var spawns = SpawnChances();

            foreach (var carType in types.carTypes.Where(t => t != null && t.liveries.Any(l => !l.isHidden)))
            {
                string kindId = carType.kind != null ? carType.kind.id : "Other";
                string kindMenu = ContentIds.RollingStock + ":" + kindId;
                store.Add(Menu(kindMenu, Localized(carType.kind?.localizationKey, Capitalize(kindId)), ContentIds.RollingStock, 100));

                var page = new InfoPageData(ContentIds.CarType(carType.id), Localized(carType.localizationKey, carType.id), kindMenu);
                AddCatalogueData(page, carType, carType.liveries.First(l => !l.isHidden));
                if (spawns.TryGetValue(carType, out var stations))
                    AddSpawns(page, stations);
                if (types.CarTypeToLoadableCargo.TryGetValue(carType, out var cargos) && cargos.Count > 0)
                    page.Blocks.Add(InfoBlock.Links("Carries", cargos.Select(c => ContentIds.CargoType(c.id))));
                store.Add(page);

                // Live data names cars by livery ID.
                foreach (var livery in carType.liveries)
                    store.AddAlias(ContentIds.CarType(livery.id), page.Id);
            }
        }

        /// <summary>Mirrors the game's vehicle catalogue booklet.</summary>
        private static void AddCatalogueData(InfoPageData page, TrainCarType_v2 carType, TrainCarLivery livery)
        {
            page.Blocks.Add(InfoBlock.Field("Mass empty", $"{carType.mass / 1000f:F1} t"));
            // Fuel, water and coal count toward the full mass, as in the catalogue.
            var loads = livery.prefab != null ? livery.prefab.GetComponentsInChildren<IDefaultMassProvider>() : new IDefaultMassProvider[0];
            if (loads.Length > 0)
                page.Blocks.Add(InfoBlock.Field("Mass full", $"{(carType.mass + loads.Sum(l => l.DefaultMassValue())) / 1000f:F1} t"));
            if (carType.wheelRadius > 0f)
                page.Blocks.Add(InfoBlock.Field("Wheel diameter", $"{carType.wheelRadius * 2f:F2} m"));
            var damage = carType.damage;
            if (damage != null && damage.bodyPrice >= 0f)
            {
                float value = damage.bodyPrice + Math.Max(0f, damage.wheelsPrice) + Math.Max(0f, damage.electricalPowertrainPrice)
                    + Math.Max(0f, damage.mechanicalPowertrainPrice);
                page.Blocks.Add(InfoBlock.Field("Value", Money(value)));
            }

            var brakes = carType.brakes;
            if (brakes != null)
            {
                page.Blocks.Add(InfoBlock.Heading("Brakes"));
                page.Blocks.Add(InfoBlock.Field("Train brake", brakes.trainBrake == TrainCarType_v2.BrakesSetup.TrainBrake.None ? "none"
                    : brakes.trainBrake == TrainCarType_v2.BrakesSetup.TrainBrake.SelfLap ? "self-lapping" : "manual lap"));
                page.Blocks.Add(InfoBlock.Field("Independent brake", brakes.hasIndependentBrake ? "yes" : "no"));
                page.Blocks.Add(InfoBlock.Field("Compressor", brakes.hasCompressor ? "yes" : "no"));
                page.Blocks.Add(InfoBlock.Field("Handbrake", brakes.hasHandbrake ? "yes" : "no"));
            }

            var licenses = LicenseChain(livery.requiredLicense);
            if (licenses.Count > 0)
            {
                page.Blocks.Add(InfoBlock.Heading("Licenses"));
                foreach (var (name, price) in licenses)
                    page.Blocks.Add(InfoBlock.Field(name, price > 0f ? Money(price) : "free"));
            }

            if (Globals.G.Types.CarLiveryToGarageRequirement.TryGetValue(livery, out var garage))
            {
                page.Blocks.Add(InfoBlock.Heading("Garage"));
                var key = SingletonBehaviour<GlobalShopController>.Instance?.shopItemsData
                    .FirstOrDefault(i => i.item != null && i.item.TryGetComponent<GaragePadlockKey>(out var k) && k.garage == garage);
                if (key != null)
                    page.Blocks.Add(InfoBlock.Field("Garage key", Money(key.basePrice)));
                page.Blocks.Add(InfoBlock.Field("Summon", Money(garage.summonPrice)));
            }
        }

        /// <summary>Chance per station that a loco type stands there. A track picks one of its groups at random.</summary>
        private static Dictionary<TrainCarType_v2, Dictionary<string, double>> SpawnChances() =>
            _spawnChances ?? (_spawnChances = ReadSpawnChances());

        // Finding the spawners takes about 75 ms, so it runs behind the loading screen instead of at tablet boot.
        private static Dictionary<TrainCarType_v2, Dictionary<string, double>> _spawnChances;

        internal static void OnWorldLoaded()
        {
            // Must not throw into the game's loading routine.
            try
            {
                using (Timing.Measure("Reading the spawn chances"))
                    _spawnChances = ReadSpawnChances();
            }
            catch (Exception ex)
            {
                _spawnChances = null;
                RATMain.LogWarning($"Could not read the spawn chances: {ex.Message}");
            }
        }

        private static Dictionary<TrainCarType_v2, Dictionary<string, double>> ReadSpawnChances()
        {
            var notFound = new Dictionary<TrainCarType_v2, Dictionary<string, double>>();
            // FindObjectsOfTypeAll also returns prefabs.
            foreach (var spawner in Resources.FindObjectsOfTypeAll<StationLocoSpawner>().Where(s => s.gameObject.scene.IsValid()))
            {
                var groups = spawner.locoTypeGroupsToSpawn?.Where(g => g?.liveries != null).ToList();
                string station = StationOf(spawner);
                if (groups == null || groups.Count == 0 || station == null) continue;

                foreach (var type in groups.SelectMany(g => g.liveries).Where(l => l != null).Select(l => l.parentType).Distinct())
                {
                    double chance = groups.Count(g => g.liveries.Any(l => l != null && l.parentType == type)) / (double)groups.Count;
                    if (!notFound.TryGetValue(type, out var stations))
                        notFound[type] = stations = new Dictionary<string, double>();
                    // Stored as the chance of not finding it, multiplied over the tracks.
                    stations[station] = (stations.TryGetValue(station, out double before) ? before : 1d) * (1d - chance);
                }
            }
            return notFound.ToDictionary(t => t.Key, t => t.Value.ToDictionary(s => s.Key, s => 1d - s.Value));
        }

        /// <summary>Yard ID of a spawner's track, e.g. "HB" for "[Y]_[HB]_[...]".</summary>
        private static string StationOf(StationLocoSpawner spawner)
        {
            var match = SpawnTrackName.Match(spawner.locoSpawnTrackName ?? "");
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static readonly Regex SpawnTrackName = new Regex(@"\[Y\]_\[([a-z0-9]*)\]_", RegexOptions.IgnoreCase);

        private static void AddSpawns(InfoPageData page, Dictionary<string, double> stations)
        {
            page.Blocks.Add(InfoBlock.Heading("Found at"));
            foreach (var station in stations.OrderByDescending(s => s.Value).ThenBy(s => s.Key))
                page.Blocks.Add(InfoBlock.Field(Stations.Name(station.Key), $"{station.Value * 100:F0} %"));
        }

        /// <summary>Job licenses before general ones, each list starting at the first to buy.</summary>
        private static List<(string name, float price)> LicenseChain(GeneralLicenseType_v2 license)
        {
            var general = new List<GeneralLicenseType_v2>();
            var job = new List<JobLicenseType_v2>();
            var openGeneral = new Stack<GeneralLicenseType_v2>();
            var openJob = new Stack<JobLicenseType_v2>();
            if (license != null) openGeneral.Push(license);
            while (openGeneral.Count > 0 || openJob.Count > 0)
            {
                if (openGeneral.Count > 0)
                {
                    var g = openGeneral.Pop();
                    // Also guards against requirement loops.
                    if (!general.Contains(g))
                    {
                        general.Add(g);
                        if (g.requiredGeneralLicense != null) openGeneral.Push(g.requiredGeneralLicense);
                        if (g.requiredJobLicense != null) openJob.Push(g.requiredJobLicense);
                    }
                }
                if (openJob.Count > 0)
                {
                    var j = openJob.Pop();
                    if (!job.Contains(j))
                    {
                        job.Add(j);
                        if (j.requiredGeneralLicense != null) openGeneral.Push(j.requiredGeneralLicense);
                        if (j.requiredJobLicense != null) openJob.Push(j.requiredJobLicense);
                    }
                }
            }
            general.Reverse();
            job.Reverse();
            return job.Select(j => (Localized(j.localizationKey, j.id), j.price))
                .Concat(general.Select(g => (Localized(g.localizationKey, g.id), g.price)))
                .ToList();
        }

        private static string Money(float amount) => "$" + amount.ToString("N0", CultureInfo.InvariantCulture);

        #endregion

        private static void Append(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<string>();
            if (!list.Contains(value))
                list.Add(value);
        }

        /// <summary>Falls back to the type's name, then the livery ID.</summary>
        internal static string LiveryName(TrainCarLivery livery) => livery == null ? null
            : Localized(livery.localizationKey, Localized(livery.parentType?.localizationKey, livery.id));

        /// <summary>Null for <see cref="CargoType.None"/>.</summary>
        internal static string CargoName(CargoType cargo)
        {
            if (cargo == CargoType.None) return null;
            return Globals.G.Types.CargoType_to_v2.TryGetValue(cargo, out var v2) ? Localized(v2.localizationKeyFull, v2.id) : cargo.ToString();
        }

        // Caches because other mods' Harmony prefixes on the translation call cost about 1 ms per data refresh.
        // Missing keys stay uncached: the language may not be loaded yet.
        private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>();

        internal static void ClearTranslations() => Translations.Clear();

        internal static string Localized(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            if (!Translations.TryGetValue(key, out string text))
            {
                if (!LocalizationAPI.HasTranslation(key)) return fallback;
                text = LocalizationAPI.L(key);
                if (string.IsNullOrWhiteSpace(text)) return fallback;
                Translations[key] = text;
            }
            return text;
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text);
    }
}
