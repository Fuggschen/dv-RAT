using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Stretches such as Catenary neutral sections or track under its wire, found from samples and joined over the route.</summary>
    public static class TrackStretches
    {
        /// <summary>Stretches in m from the track's start. Sample i lies in the middle of [i·step, (i+1)·step].</summary>
        public static List<(float From, float To)> FromSamples(IList<bool> inside, float step, float span)
        {
            var stretches = new List<(float From, float To)>();
            int start = -1;
            for (int i = 0; i <= inside.Count; i++)
            {
                bool here = i < inside.Count && inside[i];
                if (here && start < 0) start = i;
                if (!here && start >= 0)
                {
                    stretches.Add((start * step, Math.Min(i * step, span)));
                    start = -1;
                }
            }
            return stretches;
        }

        /// <summary>Like <see cref="FromSamples"/> for valued samples, e.g. voltage: one stretch per run of a value, 0 is outside.</summary>
        public static List<(float From, float To, int Value)> Runs(IList<int> values, float step, float span)
        {
            var runs = new List<(float From, float To, int Value)>();
            int start = 0;
            for (int i = 1; i <= values.Count; i++)
            {
                if (i < values.Count && values[i] == values[start]) continue;
                if (values[start] != 0)
                    runs.Add((start * step, Math.Min(i * step, span), values[start]));
                start = i;
            }
            return runs;
        }

        /// <summary>Value of the first stretch that runs past <paramref name="at"/> and begins within <paramref name="within"/> m after it, else 0.</summary>
        public static int ValueAfter(IEnumerable<(float From, float To, int Value)> stretches, float at, float within) =>
            stretches.Where(s => s.To > at && s.From <= at + within).OrderBy(s => s.From).Select(s => s.Value).FirstOrDefault();

        /// <summary>
        /// Begin and end points in m from the car, in any order. Stretches closer than <paramref name="join"/> m are one,
        /// so a track end does not break them. No begin for a section the car is in, no end past <paramref name="scanned"/>.
        /// </summary>
        public static List<(float At, bool Begins)> Points(IEnumerable<(float From, float To)> stretches, float scanned, float join)
        {
            var merged = new List<(float From, float To)>();
            foreach (var s in stretches.OrderBy(s => s.From))
            {
                if (merged.Count > 0 && s.From - merged[merged.Count - 1].To <= join)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].From, Math.Max(merged[merged.Count - 1].To, s.To));
                else
                    merged.Add(s);
            }

            var points = new List<(float At, bool Begins)>();
            foreach (var s in merged.Where(s => s.To > 0f))
            {
                if (s.From > 0f) points.Add((s.From, true));
                if (s.To < scanned) points.Add((s.To, false));
            }
            return points;
        }
    }
}
