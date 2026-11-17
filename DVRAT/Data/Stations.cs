using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Stations of the loaded map plus the rural stations of Passenger Jobs.</summary>
    public static class Stations
    {
        public static IReadOnlyList<KeyValuePair<string, string>> All() =>
            StationController.allStations
                .Where(s => s?.stationInfo != null)
                .Select(s => new KeyValuePair<string, string>(s.stationInfo.YardID, s.stationInfo.Name))
                .Concat(RuralStations.All().Select(s => new KeyValuePair<string, string>(s.Id, s.Name)))
                .OrderBy(s => s.Value)
                .ToList();

        public static string Name(string yardId)
        {
            if (string.IsNullOrEmpty(yardId)) return "";
            var station = StationController.allStations.FirstOrDefault(s => s?.stationInfo?.YardID == yardId);
            return station != null ? station.stationInfo.Name : RuralStations.NameOf(yardId) ?? yardId;
        }
    }
}
