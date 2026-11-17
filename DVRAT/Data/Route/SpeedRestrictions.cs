using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Damaged track that lowers the limit, in m from the car.</summary>
    public class SpeedRestriction
    {
        public float From;
        public float To;

        /// <summary>0: the track is closed.</summary>
        public int LimitKmh;

        /// <summary>Of the most damaged part.</summary>
        public int DamagePercent;
    }

    /// <summary>Lays restrictions over the sign limits. No Unity types, for unit tests.</summary>
    public static class SpeedRestrictions
    {
        /// <summary>
        /// Restrictions closer than <paramref name="join"/> m become one with the lowest limit and highest damage,
        /// since one damaged spot is many short sections. Ones behind the car are dropped.
        /// </summary>
        public static List<SpeedRestriction> Merge(IEnumerable<SpeedRestriction> restrictions, float join)
        {
            var merged = new List<SpeedRestriction>();
            foreach (var r in restrictions.Where(r => r.To > 0f).OrderBy(r => r.From))
            {
                var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && r.From - last.To <= join)
                {
                    last.To = Math.Max(last.To, r.To);
                    last.LimitKmh = Math.Min(last.LimitKmh, r.LimitKmh);
                    last.DamagePercent = Math.Max(last.DamagePercent, r.DamagePercent);
                }
                else
                {
                    merged.Add(new SpeedRestriction { From = r.From, To = r.To, LimitKmh = r.LimitKmh, DamagePercent = r.DamagePercent });
                }
            }
            return merged;
        }

        /// <summary>
        /// Inside a restriction the lower of its limit and the sign's applies, its own where no sign is known (0).
        /// A closed stretch also gets 0, told apart from unknown by the restriction.
        /// </summary>
        public static List<RouteSection> Apply(IReadOnlyList<RouteSection> sections, IReadOnlyList<SpeedRestriction> restrictions, float end)
        {
            if (restrictions.Count == 0) return sections.ToList();

            var cuts = sections.Select(s => s.Start)
                .Concat(restrictions.SelectMany(r => new[] { Math.Max(0f, r.From), r.To }))
                .Where(at => at < end)
                .Distinct()
                .OrderBy(at => at);
            var changes = new List<(float at, float value)>();
            foreach (float at in cuts)
            {
                float sign = sections.LastOrDefault(s => s.Start <= at)?.Value ?? 0f;
                var inForce = restrictions.Where(r => r.From <= at && at < r.To).ToList();
                float limit = inForce.Count > 0 ? inForce.Min(r => r.LimitKmh) : 0f;
                changes.Add((at, inForce.Count == 0 ? sign : sign > 0f ? Math.Min(sign, limit) : limit));
            }
            return RouteProfile.Sections(changes, end);
        }

        /// <summary>Null when the car is in none.</summary>
        public static SpeedRestriction AtCar(IEnumerable<SpeedRestriction> restrictions) =>
            restrictions.FirstOrDefault(r => r.From <= 0f && r.To > 0f);
    }
}
