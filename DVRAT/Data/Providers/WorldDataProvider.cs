using System;
using System.Collections.Generic;
using System.Linq;
using DV.InventorySystem;
using DV.Logic.Job;
using DV.ServicePenalty;
using DV.Utils;
using DV.WeatherSystem;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Reads game time, weather, money, licenses and the nearest station into a <see cref="WorldData"/>.</summary>
    public class WorldDataProvider : IDisposable
    {
        /// <param name="car">Its train is marked on the station's tracks.</param>
        /// <param name="pickedStation">Yard ID of the picked station. Empty for the nearest.</param>
        /// <param name="readFees">False while no shown page needs the fees, which take long to read.</param>
        public WorldData Refresh(TrainCar car, string pickedStation, bool readFees)
        {
            var data = new WorldData();
            // Each part on its own, so a system that is not loaded yet does not hide the others.
            Try("weather", () => ReadWeather(data));
            Try("forecast", () => ReadForecast(data));
            // Must follow the forecast, which it adds temperatures to.
            DVSeasonsWeather.Read(data);
            data.Integrations = new List<IntegrationStatus>
            {
                new IntegrationStatus { Name = "1500 V DC Catenary (2WE3)", State = CatenaryMods.WE3State },
                new IntegrationStatus { Name = "1500 V DC Catenary (wire)", State = CatenaryWires.State },
                new IntegrationStatus { Name = "Catenary for CCL", State = CatenaryMods.CclState },
                new IntegrationStatus { Name = "DVSeasons", State = DVSeasonsWeather.State },
                new IntegrationStatus { Name = "Junction Maintenance", State = JunctionDamage.State },
                new IntegrationStatus { Name = "Passenger Jobs", State = RuralStations.State },
                new IntegrationStatus { Name = "Persistent Jobs", State = SuspendedCars.State },
                new IntegrationStatus { Name = "Realistic Brakes", State = RealisticBrakes.State },
                new IntegrationStatus { Name = "Realistic Firing", State = RealisticFiring.State },
                new IntegrationStatus { Name = "Solar Gadgets", State = SolarGadgetsPower.State },
                new IntegrationStatus { Name = "Track Maintenance", State = TrackDamage.State },
                new IntegrationStatus { Name = "WE-4U (traction battery)", State = WE4UBattery.State },
            };
            Try("money", () => data.Money = SingletonBehaviour<Inventory>.Instance.PlayerMoney);
            Try("licenses", () => ReadLicenses(data));
            if (readFees)
                Try("fees", () => ReadFees(data));
            Try("owned cars", () => ReadOwnedCars(data));
            Try("station", () => ReadStation(data, car, pickedStation));
            return data;
        }

        private static void ReadWeather(WorldData data)
        {
            var weather = SingletonBehaviour<WeatherDriver>.Instance;
            if (weather == null || weather.manager == null) return;
            data.GameTime = weather.manager.DateTime;
            data.IsDay = weather.IsDay;
            float dayMinutes = weather.DayLengthInMinutes;
            if (dayMinutes > 0f)
                data.TimeScale = 24f * 60f / dayMinutes;
            data.Rain = weather.RainValue;
            data.Wetness = weather.WetnessValue;
        }

        /// <summary>The game's daily 06:00 forecast. Slots know only their hour, so their date is counted from the forecast time.</summary>
        private static void ReadForecast(WorldData data)
        {
            var forecaster = SingletonBehaviour<WeatherForecaster>.Instance;
            if (forecaster == null || !forecaster.HasValidForecastForToday()) return;
            var start = forecaster.lastForecastTimestamp.Value.Date.AddHours(forecaster.doForecastAt);
            float hours = forecaster.groupByNHours;
            var items = forecaster.interpretedData;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                data.Forecast.Add(new ForecastSlot
                {
                    Start = start.AddHours(i * hours),
                    Hours = hours,
                    Icon = item.iconType.ToString(),
                    CloudPct = item.averageCloudiness,
                    RainPct = item.averageRain,
                    FogPct = item.averageFog,
                    ThunderPct = item.averageThunder,
                });
            }
        }

        private static void ReadLicenses(WorldData data)
        {
            var licenses = SingletonBehaviour<LicenseManager>.Instance;
            if (licenses == null) return;
            data.Licenses.AddRange(licenses.GetAcquiredJobLicenses()
                .Select(l => new License { Name = GameContent.Localized(l.localizationKey, l.id), Kind = "job" })
                .OrderBy(l => l.Name));
            data.Licenses.AddRange(licenses.GetGeneralAcquiredLicenses()
                .Select(l => new License { Name = GameContent.Localized(l.localizationKey, l.id), Kind = "general" })
                .OrderBy(l => l.Name));
        }

        /// <summary>Adds the fees to data read without them. In multiplayer only the host has them, and a client gets them from the host.</summary>
        public static void ReadFees(WorldData data)
        {
            data.Fees.AddRange(SharedFees.Fees());
            data.FeesRead = true;
        }

        private static void ReadOwnedCars(WorldData data)
        {
            var owned = SingletonBehaviour<OwnedCarsStateController>.Instance;
            if (owned == null) return;
            owned.RefreshOwnedCarsStatesData();
            var player = PlayerManager.PlayerTransform;
            for (int i = 0; i < owned.NumberOfCarStates; i++)
            {
                var state = owned.GetIthSortedVehicleState(i);
                if (state == null) continue;
                var entry = new OwnedCar { Id = state.ID, ServicePrice = state.GetTotalPrice(), Destroyed = state.IsStaged };
                if (state is ExistingOwnedCarDebt existing && existing.car != null)
                {
                    entry.Type = existing.car.carLivery?.id;
                    entry.Name = GameContent.LiveryName(existing.car.carLivery);
                    entry.Track = existing.car.logicCar?.CurrentTrack?.ID.FullDisplayID;
                    entry.Distance = player != null ? Vector3.Distance(player.position, existing.car.transform.position) : 0f;
                }
                data.OwnedCars.Add(entry);
            }
        }

        private static void ReadStation(WorldData data, TrainCar car, string pickedStation)
        {
            StationController nearest = null, picked = null;
            float nearestSqr = float.MaxValue, pickedSqr = 0f;
            foreach (var station in StationController.allStations ?? new List<StationController>())
            {
                var range = station != null ? station.GetComponent<StationJobGenerationRange>() : null;
                string yardId = station != null ? station.stationInfo?.YardID : null;
                if (range == null || string.IsNullOrEmpty(yardId)) continue;
                float sqr = range.PlayerSqrDistanceFromStationCenter;
                data.Stations.Add(new StationEntry { Id = yardId, Distance = Mathf.Sqrt(sqr) });
                if (yardId == pickedStation)
                {
                    picked = station;
                    pickedSqr = sqr;
                }
                if (sqr < nearestSqr)
                {
                    nearest = station;
                    nearestSqr = sqr;
                }
            }
            if (picked != null)
            {
                nearest = picked;
                nearestSqr = pickedSqr;
                data.StationPicked = true;
            }
            if (nearest == null) return;

            data.NearestStationId = nearest.stationInfo?.YardID;
            data.NearestStationDistance = Mathf.Sqrt(nearestSqr);
            data.AtStation = nearest.GetComponent<StationJobGenerationRange>().IsPlayerInJobGenerationZone(nearestSqr);

            data.JobsKnown = data.AtStation || SuspendedCars.Active;
            if (data.JobsKnown && nearest.logicStation?.availableJobs != null)
            {
                data.StationJobs = nearest.logicStation.availableJobs.Where(j => j != null).Select(JobDataProvider.BuildJobInfo).ToList();
                foreach (var job in data.StationJobs)
                    job.IsActive = ActiveJobStore.IsActive(job.JobId);
            }

            ReadStationTracks(data, nearest, car);
            ReadStationSwitches(data, data.NearestStationId);
            ReadStationTrackDamage(data, nearest);
        }

        // Cached because they do not change while a save is loaded.
        private static readonly Dictionary<string, List<Junction>> SwitchesByYard = new Dictionary<string, List<Junction>>();

        private static void ReadStationSwitches(WorldData data, string yardId)
        {
            data.SwitchesKnown = JunctionDamage.State == IntegrationState.Active;
            if (!data.SwitchesKnown || string.IsNullOrEmpty(yardId)) return;

            data.StationSwitches = StationSwitches(yardId).Select(JunctionDamage.Read).Where(s => s != null)
                .OrderByDescending(s => s.Damage).ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The switches named after the yard, e.g. "S-0228-HB" for HB.</summary>
        private static List<Junction> StationSwitches(string yardId)
        {
            if (SwitchesByYard.TryGetValue(yardId, out var switches) && switches.All(j => j != null))
                return switches;
            switches = SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks
                .Where(t => t != null)
                .SelectMany(t => new[] { t.inJunction, t.outJunction })
                .Where(j => j != null && j.StationOf() == yardId)
                .Distinct()
                .ToList();
            SwitchesByYard[yardId] = switches;
            return switches;
        }

        /// <summary>Track Maintenance lists a switch's tracks under the switch ID, so they share its name here.</summary>
        private static void ReadStationTrackDamage(WorldData data, StationController station)
        {
            data.TrackDamageKnown = TrackDamage.State == IntegrationState.Active;
            string yardId = station.stationInfo?.YardID;
            if (!data.TrackDamageKnown || string.IsNullOrEmpty(yardId)) return;

            var named = StationRails(station).Where(t => t != null)
                .Select(t => (track: t, name: t.LogicTrack()?.ID.FullDisplayID ?? t.name));
            var throughSwitches = StationSwitches(yardId)
                .SelectMany(j => (j.outBranches ?? new List<Junction.Branch>())
                    .Where(b => b?.track != null)
                    .Select(b => (track: b.track, name: j.Name())));
            foreach (var (track, name) in named.Concat(throughSwitches).GroupBy(t => t.track).Select(g => g.First()))
                foreach (var section in TrackDamage.On(track))
                {
                    section.Track = name;
                    data.StationTrackDamage.Add(section);
                }
            data.StationTrackDamage = data.StationTrackDamage
                .OrderByDescending(s => s.DamagePercent).ThenBy(s => s.Track, StringComparer.Ordinal).ThenBy(s => s.From)
                .ToList();
        }

        // Jobs of cars on the shown station's tracks, by ID. Suspended cars' jobs are in no station's list.
        private static readonly Dictionary<string, Job> TrackJobs = new Dictionary<string, Job>();

        /// <summary>Null once the job is handed in, abandoned or expired.</summary>
        internal static JobInfo FindJob(string jobId, out bool taken)
        {
            taken = false;
            if (string.IsNullOrEmpty(jobId)) return null;
            if (!TrackJobs.TryGetValue(jobId, out var job))
                job = (JobsManager.Instance?.currentJobs ?? new List<Job>())
                    .Concat(StationController.allStations.SelectMany(s => s?.logicStation?.availableJobs ?? new List<Job>()))
                    .FirstOrDefault(j => j != null && j.ID == jobId);
            if (job == null || !IsOpen(job))
                return null;

            taken = job.State == DV.ThingTypes.JobState.InProgress;
            var info = JobDataProvider.BuildJobInfo(job);
            info.IsActive = ActiveJobStore.IsActive(jobId);
            return info;
        }

        private static bool IsOpen(Job job) =>
            job.State == DV.ThingTypes.JobState.Available || job.State == DV.ThingTypes.JobState.InProgress;

        // Cached because they do not change while a save is loaded.
        private static readonly Dictionary<StationController, List<RailTrack>> RailsByStation = new Dictionary<StationController, List<RailTrack>>();

        /// <summary>The game's AllStationTracks has only job tracks, so sub-yards such as CW's B yard are missing.</summary>
        private static List<RailTrack> StationRails(StationController station)
        {
            if (RailsByStation.TryGetValue(station, out var rails) && rails.All(t => t != null))
                return rails;
            string yardId = station.stationInfo?.YardID;
            rails = SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks
                .Where(t => t != null)
                .Where(t =>
                {
                    var id = t.LogicTrack()?.ID;
                    return id != null && !id.IsGeneric() && id.yardId == yardId;
                })
                .ToList();
            RailsByStation[station] = rails;
            return rails;
        }

        /// <summary>Tracks are counted from the side the longest track starts at, so the tablet's bars line up.</summary>
        private static void ReadStationTracks(WorldData data, StationController station, TrainCar car)
        {
            var tracks = new Dictionary<RailTrack, (TrackSpace space, bool flip)>();
            // Only closed jobs go, because another tablet may show another station.
            foreach (var id in TrackJobs.Where(entry => !IsOpen(entry.Value)).Select(entry => entry.Key).ToList())
                TrackJobs.Remove(id);
            var rails = StationRails(station)
                .Where(t => t != null && t.LogicTrack() != null)
                .OrderByDescending(t => t.GetKinkedPointSet().span)
                .ToList();
            Vector3? yardAxis = null;
            foreach (var railTrack in rails)
            {
                var track = railTrack.LogicTrack();
                var points = railTrack.GetKinkedPointSet();
                var direction = TrackGeometry.ToVector3(points.points[points.points.Length - 1].position) - TrackGeometry.ToVector3(points.points[0].position);
                if (!yardAxis.HasValue)
                    yardAxis = direction;
                var space = new TrackSpace
                {
                    Id = track.ID.FullDisplayID,
                    Group = track.ID.SignIDSubYardPart ?? "",
                    Length = (float)track.length,
                    RailLength = (float)points.span,
                    Free = Mathf.Max(0f, (float)track.length - track.OccupiedLength),
                };
                tracks[railTrack] = (space, Vector3.Dot(direction, yardAxis.Value) < 0f);
                data.StationTracks.Add(space);
            }
            data.StationTracks.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (tracks.Count == 0) return;

            var train = car != null && car.trainset != null ? new HashSet<TrainCar>(car.trainset.cars) : new HashSet<TrainCar>();
            var jobs = JobsManager.Instance;
            foreach (var trainCar in SingletonBehaviour<CarSpawner>.Instance.AllCars)
            {
                var bogies = trainCar != null ? trainCar.Bogies : null;
                if (bogies == null || bogies.Length == 0) continue;
                foreach (var entry in bogies.Where(b => b != null && b.track != null && b.traveller != null && !b.HasDerailed && tracks.ContainsKey(b.track)).GroupBy(b => b.track))
                {
                    var (space, flip) = tracks[entry.Key];
                    var spans = entry.Select(b => (float?)(float)b.traveller.Span).ToList();
                    var (from, to) = TrackCar.Extent(spans[0], spans.Count > 1 ? spans[1] : null,
                        trainCar.logicCar != null ? trainCar.logicCar.length : 0f, space.RailLength, flip);
                    var job = jobs != null && trainCar.logicCar != null ? jobs.GetJobOfCar(trainCar.logicCar, false) : null;
                    if (job != null)
                        TrackJobs[job.ID] = job;
                    space.Cars.Add(new TrackCar
                    {
                        Id = trainCar.ID,
                        Name = GameContent.LiveryName(trainCar.carLivery),
                        Cargo = trainCar.logicCar != null ? GameContent.CargoName(trainCar.logicCar.CurrentCargoTypeInCar) : null,
                        JobId = job?.ID,
                        From = from,
                        To = to,
                        Kind = train.Contains(trainCar) ? TrackCarKind.Train
                            : job != null && ActiveJobStore.IsActive(job.ID) ? TrackCarKind.Planned
                            : trainCar.IsLoco ? TrackCarKind.Loco
                            : TrackCarKind.Other,
                    });
                }
            }
            AddSuspendedCars(station, tracks);
            foreach (var space in data.StationTracks)
                space.Cars.Sort((a, b) => a.From.CompareTo(b.From));
        }

        /// <summary>Cars Persistent Jobs took out of the world while the player is far away. They still stand on the tracks.</summary>
        private static void AddSuspendedCars(StationController station, Dictionary<RailTrack, (TrackSpace space, bool flip)> tracks)
        {
            foreach (var car in SuspendedCars.AtStation(station.stationInfo?.YardID))
            {
                var bogies = new[] { (track: car.Track1, span: car.Span1), (track: car.Track2, span: car.Span2) };
                foreach (var entry in bogies.Where(b => b.track != null && tracks.ContainsKey(b.track)).GroupBy(b => b.track))
                {
                    var (space, flip) = tracks[entry.Key];
                    var spans = entry.Select(b => (float?)b.span).ToList();
                    var (from, to) = TrackCar.Extent(spans[0], spans.Count > 1 ? spans[1] : null, car.Length, space.RailLength, flip);
                    if (car.Job != null)
                        TrackJobs[car.Job.ID] = car.Job;
                    space.Cars.Add(new TrackCar
                    {
                        Id = car.Id,
                        Name = car.Name,
                        Cargo = car.Cargo,
                        JobId = car.Job?.ID,
                        From = from,
                        To = to,
                        Kind = car.Job != null && ActiveJobStore.IsActive(car.Job.ID) ? TrackCarKind.Planned
                            : car.IsLoco ? TrackCarKind.Loco
                            : TrackCarKind.Other,
                    });
                }
            }
        }

        private static void Try(string part, Action read)
        {
            try
            {
                read();
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"WorldDataProvider: reading {part} failed: {ex.Message}");
            }
        }

        public void Dispose() { }
    }
}
