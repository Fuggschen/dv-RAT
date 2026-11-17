using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.ThingTypes;
using DV.Utils;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    /// <summary>
    /// Reads cars suspended by Persistent Jobs (github.com/Banjobeni/DerailValley-PersistentJobs), which the game's car list misses.
    /// They sit as save data in static dictionaries of FarCarOpt, read by reflection. A missing member switches the reader off with one log warning.
    /// </summary>
    public static class SuspendedCars
    {
        private static readonly ModWatch Mod = new ModWatch("PersistentJobsMod");
        private const string FarCarOptTypeName = "PersistentJobsMod.Optimization.FarCarOpt";

        /// <summary>A suspended car, placed by where its bogies stood.</summary>
        public class Car
        {
            public string Id;
            public RailTrack Track1;
            public float Span1;
            public RailTrack Track2;
            public float Span2;
            public float Length;
            public bool IsLoco;

            /// <summary>Localized.</summary>
            public string Name;

            /// <summary>Localized. Null when empty.</summary>
            public string Cargo;

            public DV.Logic.Job.Job Job;
        }

        // Length guess for car types the mod has no length for: bogie distance plus this.
        private const float Overhangs = 4f;

        private static bool _searched;
        private static bool _failed;
        private static FieldInfo _carObjects;
        private static FieldInfo _jobChains;
        private static FieldInfo _lengths;
        private static FieldInfo _byStation;

        /// <summary>True while the mod is loaded. It also keeps job offers of far stations.</summary>
        public static bool Active => Find();

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        public static List<Car> AtStation(string yardId)
        {
            var cars = new List<Car>();
            if (string.IsNullOrEmpty(yardId) || !Find()) return cars;
            try
            {
                var byStation = _byStation.GetValue(null) as IDictionary;
                var carObjects = _carObjects.GetValue(null) as IDictionary;
                var jobChains = _jobChains.GetValue(null) as IDictionary;
                var lengths = _lengths.GetValue(null) as IDictionary;
                if (byStation == null || carObjects == null || !byStation.Contains(yardId)) return cars;

                var tracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.OrderedRailtracks;
                foreach (string guid in (IEnumerable)byStation[yardId])
                {
                    if (!(carObjects[guid] is JObject data)) continue;
                    var car = Read(data, guid, tracks, lengths);
                    if (car == null) continue;
                    var chain = jobChains != null && jobChains.Contains(guid) ? jobChains[guid] as JobChainController : null;
                    car.Job = chain?.currentJobInChain;
                    cars.Add(car);
                }
            }
            catch (Exception ex)
            {
                Fail($"reading its suspended cars failed: {(ex.InnerException ?? ex).Message}");
            }
            return cars;
        }

        private static Car Read(JObject data, string guid, RailTrack[] tracks, IDictionary lengths)
        {
            int track1 = (int?)data["bog1TrackChildInd"] ?? -1;
            int track2 = (int?)data["bog2TrackChildInd"] ?? -1;
            if (track1 < 0 || track2 < 0 || track1 >= tracks.Length || track2 >= tracks.Length) return null;

            var car = new Car
            {
                Id = (string)data["id"],
                Track1 = tracks[track1],
                Span1 = (float?)data["bog1PosOnTrack"] ?? 0f,
                Track2 = tracks[track2],
                Span2 = (float?)data["bog2PosOnTrack"] ?? 0f,
            };
            var type = (TrainCarType)((int?)data["type"] ?? 0);
            car.IsLoco = type.ToString().StartsWith("Loco", StringComparison.Ordinal);
            car.Length = lengths != null && lengths.Contains(type) && lengths[type] is float length
                ? length
                : (car.Track1 == car.Track2 ? Math.Abs(car.Span1 - car.Span2) : 0f) + Overhangs;

            // Prefer the game's own record of the car, if it still has one.
            DV.Logic.Job.Car logicCar = null;
            var ids = SingletonBehaviour<DV.Logic.Job.IdGenerator>.Instance;
            if (ids != null && guid != null)
                ids.carGuidToCar.TryGetValue(guid, out logicCar);
            car.Name = GameContent.LiveryName(logicCar != null ? logicCar.carType
                : DV.Globals.G.Types.TrainCarType_to_v2.TryGetValue(type, out var livery) ? livery : null);
            car.Cargo = GameContent.CargoName(logicCar != null ? logicCar.CurrentCargoTypeInCar : (CargoType)((int?)data["loadedCargo"] ?? 0));
            return car;
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var type = Mod.FindType(FarCarOptTypeName);
                if (type == null)
                {
                    Fail($"{FarCarOptTypeName} was not found");
                    return false;
                }
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                _carObjects = type.GetField("SuspendedCarObjects", flags);
                _jobChains = type.GetField("SuspendedCarGUIDToJobChainController", flags);
                _lengths = type.GetField("TrainCarTypeToInterCouplerDistance", flags);
                _byStation = type.GetField("StationIDtoSuspendedCarGUID", flags);
                if (_carObjects == null || _byStation == null)
                {
                    Fail("its list of suspended cars was not found");
                    return false;
                }
                RATMain.Log("Persistent Jobs found, its suspended cars are shown on the Yard page");
            }
            return _carObjects != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Persistent Jobs: {reason}. Its suspended cars are left out.");
        }
    }
}
