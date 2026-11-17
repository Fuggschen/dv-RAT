using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>
    /// Speed limits the front has passed, while the rest of the train is still in them. Positions are an odometer,
    /// meters driven since the last reset. No Unity types, for unit tests.
    /// </summary>
    public class SpeedHistory
    {
        /// <summary>Longer than any train.</summary>
        public const float KeepMeters = 3000f;

        private readonly List<RouteSection> _sections = new List<RouteSection>();

        public void Reset() => _sections.Clear();

        /// <summary>Replaces what was known about that stretch, the latest scan being the most accurate.</summary>
        public void Record(float odometer, IEnumerable<RouteSection> ahead)
        {
            _sections.RemoveAll(s => s.Start >= odometer || s.End < odometer - KeepMeters);
            foreach (var section in _sections.Where(s => s.End > odometer))
                section.End = odometer;
            foreach (var section in ahead)
                _sections.Add(new RouteSection { Start = odometer + section.Start, End = odometer + section.End, Value = section.Value });
        }

        /// <summary>At negative positions, measured from <paramref name="odometer"/>.</summary>
        public List<RouteSection> Behind(float odometer) => _sections
            .Where(s => s.Start < odometer)
            .Select(s => new RouteSection { Start = s.Start - odometer, End = System.Math.Min(s.End, odometer) - odometer, Value = s.Value })
            .ToList();
    }
}
