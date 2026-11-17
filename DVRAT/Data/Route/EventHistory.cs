using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Route events the car has passed. Positions share the odometer of <see cref="SpeedHistory"/>. No Unity types, for unit tests.</summary>
    public class EventHistory
    {
        private readonly List<(float at, TrackEvent ev)> _events = new List<(float, TrackEvent)>();

        public void Reset() => _events.Clear();

        /// <summary>Replaces what was known about that stretch.</summary>
        public void Record(float odometer, IEnumerable<TrackEvent> ahead)
        {
            _events.RemoveAll(e => e.at >= odometer || e.at < odometer - SpeedHistory.KeepMeters);
            // Events at or behind the car are already stored from earlier scans.
            _events.AddRange(ahead.Where(e => e.DistanceMeters > 0f).Select(e => (odometer + e.DistanceMeters, e)));
        }

        /// <summary>The last <paramref name="count"/> passed events, nearest last, at negative distances.</summary>
        public List<TrackEvent> Behind(float odometer, int count) => _events
            .Where(e => e.at < odometer)
            .OrderBy(e => e.at)
            .Skip(System.Math.Max(0, _events.Count(e => e.at < odometer) - count))
            .Select(e => new TrackEvent
            {
                EventType = e.ev.EventType,
                Name = e.ev.Name,
                DistanceMeters = e.at - odometer,
                Value = e.ev.Value,
                StationId = e.ev.StationId,
                Track = e.ev.Track,
                Aspect = e.ev.Aspect,
                Restrictive = e.ev.Restrictive,
            })
            .ToList();
    }
}
