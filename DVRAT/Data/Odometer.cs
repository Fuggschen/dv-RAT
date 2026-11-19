using System.Collections.Generic;
using DV.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Distance every loco has run, saved per savegame by car GUID. The host or a single player counts.
    /// A client's tablet shows what the host describes, keyed by car ID as the fees are.
    /// </summary>
    public static class Odometer
    {
        private const string Key = "odometers";

        /// <summary>Car GUID to meters. Double, as a float stops adding short steps past a few thousand km.</summary>
        private static readonly Dictionary<string, double> Meters = new Dictionary<string, double>();

        private struct Reading
        {
            public Bogie Bogie;
            public double Meters;
        }

        /// <summary>Last bogie reading per car GUID, so each frame adds only the change.</summary>
        private static readonly Dictionary<string, Reading> LastReading = new Dictionary<string, Reading>();

        private static Dictionary<string, double> _receivedKm = new Dictionary<string, double>();

        private static GameObject _host;

        public static void Initialize()
        {
            if (_host != null) return;
            _host = new GameObject("RAT_Odometer");
            Object.DontDestroyOnLoad(_host);
            _host.AddComponent<OdometerBehaviour>();
        }

        public static void Shutdown()
        {
            if (_host != null) Object.Destroy(_host);
            _host = null;
        }

        /// <summary>Null on a client that has no value from the host yet.</summary>
        public static double? KmOf(TrainCar car)
        {
            if (car == null) return null;
            if (SharedFees.FromHost)
                return _receivedKm.TryGetValue(car.ID, out double km) ? km : (double?)null;
            if (car.logicCar == null) return null;
            return Meters.TryGetValue(car.CarGUID, out double meters) ? meters / 1000.0 : 0.0;
        }

        /// <summary>Reads the game's own track distance from each loco's front bogie, which ignores floating-origin shifts.</summary>
        internal static void Tick()
        {
            if (!WorldStreamingInit.IsLoaded || SharedFees.FromHost) return;
            var locos = SingletonBehaviour<CarSpawner>.Instance?.AllLocos;
            if (locos == null) return;
            foreach (var car in locos)
            {
                if (car == null || car.logicCar == null || car.Bogies == null || car.Bogies.Length == 0) continue;
                var bogie = car.FrontBogie;
                if (bogie == null) continue;
                bogie.DistanceTrackingEnabled = true;

                string guid = car.CarGUID;
                double reading = bogie.TotalDistanceTravelled;
                // A respawned car or a reset counter starts a new baseline.
                if (LastReading.TryGetValue(guid, out var last) && last.Bogie == bogie && reading >= last.Meters)
                {
                    double step = reading - last.Meters;
                    if (step > 0.0)
                    {
                        Meters.TryGetValue(guid, out double total);
                        Meters[guid] = total + step;
                    }
                }
                LastReading[guid] = new Reading { Bogie = bogie, Meters = reading };
            }
        }

        public static void WriteTo(JObject data)
        {
            var odometers = new JObject();
            foreach (var entry in Meters)
                odometers[entry.Key] = entry.Value;
            data[Key] = odometers;
        }

        public static void ReadFrom(JObject data)
        {
            Meters.Clear();
            LastReading.Clear();
            if (!(data?[Key] is JObject odometers)) return;
            foreach (var entry in odometers)
                if (entry.Value != null && (entry.Value.Type == JTokenType.Float || entry.Value.Type == JTokenType.Integer))
                    Meters[entry.Key] = (double)entry.Value;
        }

        /// <summary>Car ID to km, rounded as shown, so the JSON changes only every 100 m a loco runs.</summary>
        public static string Describe()
        {
            var km = new Dictionary<string, double>();
            foreach (var car in SingletonBehaviour<CarSpawner>.Instance?.AllLocos ?? new List<TrainCar>())
            {
                double? value = car != null ? KmOf(car) : null;
                if (value.HasValue)
                    km[car.ID] = System.Math.Round(value.Value, 1);
            }
            return JsonConvert.SerializeObject(km);
        }

        /// <summary>Takes the JSON from <see cref="Describe"/>.</summary>
        public static void Receive(string json)
        {
            try
            {
                _receivedKm = JsonConvert.DeserializeObject<Dictionary<string, double>>(json) ?? new Dictionary<string, double>();
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Multiplayer: the host's odometers could not be read: {ex.Message}");
            }
        }

        public static void Clear() => _receivedKm = new Dictionary<string, double>();
    }

    public class OdometerBehaviour : MonoBehaviour
    {
        private void Update() => Odometer.Tick();
    }
}
