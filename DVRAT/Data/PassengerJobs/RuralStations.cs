using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Logic.Job;

namespace DVRAT
{
    /// <summary>
    /// Reads the rural stations of Passenger Jobs (github.com/katycat5e/DVPassengerJobs), which <see cref="StationController.allStations"/> misses.
    /// They sit in the static RouteManager._stations, read by reflection. A missing member switches the reader off with one log warning.
    /// </summary>
    public static class RuralStations
    {
        private static readonly ModWatch Mod = new ModWatch("PassengerJobs");
        private const string RouteManagerTypeName = "PassengerJobs.Generation.RouteManager";
        private const string RuralStationTypeName = "PassengerJobs.Generation.RuralStationData";
        private const string NameKeyPrefix = "passjobs/station_";

        public class Station
        {
            public string Id;
            public string Name;
            public RailTrack Track;
        }

        private static bool _searched;
        private static bool _failed;
        private static FieldInfo _stations;
        private static FieldInfo _platform;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Empty without the mod. Platforms inside a game station are left out, as it already stands for them under the same ID.</summary>
        public static List<Station> All()
        {
            var result = new List<Station>();
            foreach (var platform in Platforms())
            {
                var track = platform.WarehouseTrack?.RailTrack();
                if (track != null)
                    result.Add(new Station { Id = platform.ID, Name = Name(platform.ID), Track = track });
            }
            return result;
        }

        /// <summary>Null when <paramref name="id"/> is not a rural station.</summary>
        public static string NameOf(string id) =>
            Platforms().Any(p => p.ID == id) ? Name(id) : null;

        private static string Name(string id) => GameContent.Localized(NameKeyPrefix + id.ToLowerInvariant(), id);

        private static IEnumerable<WarehouseMachine> Platforms()
        {
            var platforms = new List<WarehouseMachine>();
            if (!Find()) return platforms;
            try
            {
                if (!(_stations.GetValue(null) is IDictionary stations)) return platforms;
                foreach (var data in stations.Values)
                {
                    if (data == null || data.GetType().FullName != RuralStationTypeName) continue;
                    if (_platform == null)
                    {
                        _platform = data.GetType().GetField("Platform", BindingFlags.Public | BindingFlags.Instance);
                        if (_platform == null)
                        {
                            Fail("its rural station platform was not found");
                            return platforms;
                        }
                    }
                    // The mod's loading machine is a game warehouse machine. Its ID is the station ID.
                    if (_platform.GetValue(data) is WarehouseMachine platform && !string.IsNullOrEmpty(platform.ID)
                        && !StationController.allStations.Any(s => s?.stationInfo?.YardID == platform.ID))
                        platforms.Add(platform);
                }
            }
            catch (Exception ex)
            {
                Fail($"reading its rural stations failed: {(ex.InnerException ?? ex).Message}");
            }
            return platforms;
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var type = Mod.FindType(RouteManagerTypeName);
                if (type == null)
                {
                    Fail($"{RouteManagerTypeName} was not found");
                    return false;
                }
                _stations = type.GetField("_stations", BindingFlags.NonPublic | BindingFlags.Static);
                if (_stations == null)
                {
                    Fail("its list of stations was not found");
                    return false;
                }
                RATMain.Log("Passenger Jobs found, its rural stations are added to the names and the planner");
            }
            return _stations != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Passenger Jobs: {reason}. Its rural stations are left out.");
        }
    }
}
