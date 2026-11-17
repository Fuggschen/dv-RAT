using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>A stretch of route with one value, e.g. a limit or grade.</summary>
    public class RouteSection
    {
        public float Start;
        public float End;
        public float Value;

        public float Length => End - Start;
    }

    /// <summary>Turns signs along the route into sections. No Unity types, for unit tests.</summary>
    public static class RouteProfile
    {
        /// <summary>Each change applies until the next, the last until <paramref name="end"/>. Repeated values are dropped.</summary>
        public static List<RouteSection> Sections(IEnumerable<(float at, float value)> changes, float end)
        {
            var sections = new List<RouteSection>();
            foreach (var (at, value) in changes.OrderBy(c => c.at))
            {
                if (sections.Count > 0)
                {
                    var last = sections[sections.Count - 1];
                    if (last.Value == value) continue;
                    if (at <= last.Start)
                    {
                        // Two changes at the same spot: the later one wins.
                        last.Value = value;
                        continue;
                    }
                    last.End = at;
                }
                sections.Add(new RouteSection { Start = at, End = end, Value = value });
            }

            if (sections.Count > 1 && sections[sections.Count - 1].Value == sections[sections.Count - 2].Value)
                sections.RemoveAt(sections.Count - 1);
            return sections;
        }

        /// <summary>
        /// Seconds at each section's limit, ignoring acceleration and braking. Sections with limit 0 and the stretch
        /// past the last section count at <paramref name="unknownKmh"/>.
        /// </summary>
        public static double TravelSeconds(IReadOnlyList<RouteSection> sections, float distance, float unknownKmh = 40f)
        {
            double seconds = 0;
            float covered = 0f;
            foreach (var section in sections)
            {
                if (covered >= distance) break;
                float from = System.Math.Max(section.Start, covered);
                float to = System.Math.Min(section.End, distance);
                if (from > covered)
                    seconds += (from - covered) / (unknownKmh / 3.6f);
                if (to > from)
                    seconds += (to - from) / ((section.Value > 0 ? section.Value : unknownKmh) / 3.6f);
                covered = System.Math.Max(covered, to);
            }
            if (covered < distance)
                seconds += (distance - covered) / (unknownKmh / 3.6f);
            return seconds;
        }

        /// <summary>
        /// Front position (meters ahead) and new limit for each rise within <paramref name="scanned"/>. The limit is the
        /// lowest one under the train, so it rises only once the train's end leaves a lower section.
        /// <paramref name="behind"/> is at negative positions. Limit 0 is ignored.
        /// </summary>
        public static List<(float at, float limit)> TrainClearPoints(IEnumerable<RouteSection> behind,
            IEnumerable<RouteSection> ahead, float trainLength, float scanned)
        {
            var points = new List<(float, float)>();
            if (trainLength <= 0f) return points;
            var known = behind.Concat(ahead).Where(s => s.Value > 0f && s.End > s.Start).ToList();

            const float Step = 0.5f;
            foreach (float at in known.Select(s => s.End + trainLength).Where(x => x > 0f && x < scanned).Distinct().OrderBy(x => x))
            {
                float before = LimitUnderTrain(known, at - Step, trainLength);
                float after = LimitUnderTrain(known, at + Step, trainLength);
                if (before > 0f && after > before)
                    points.Add((at, after));
            }
            return points;
        }

        /// <summary>Lowest known limit under the train, 0 when none is known.</summary>
        public static float LimitUnderTrain(IEnumerable<RouteSection> sections, float front, float trainLength)
        {
            float lowest = float.MaxValue;
            foreach (var s in sections)
                if (s.Value > 0f && s.Start <= front && s.End > front - trainLength && s.Value < lowest)
                    lowest = s.Value;
            return lowest == float.MaxValue ? 0f : lowest;
        }

        /// <summary>The last section when none contains <paramref name="distance"/>.</summary>
        public static RouteSection At(IReadOnlyList<RouteSection> sections, float distance)
        {
            foreach (var section in sections)
                if (distance < section.End) return section;
            return sections.Count > 0 ? sections[sections.Count - 1] : null;
        }
    }
}
