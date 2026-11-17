using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DVRAT
{
    /// <summary>Rail distances between the places a plan visits, for <see cref="ShuntingPlanner.RouteCosts"/>. No Unity types.</summary>
    public static class StationDistances
    {
        /// <summary>Meters a turn on the line counts as.</summary>
        public const float ReversePenalty = 500f;

        /// <summary>Meters between places, leaving out the tracks inside them. Pairs without a way are omitted.</summary>
        /// <param name="places">Track indices of each place in <paramref name="graph"/>.</param>
        public static Dictionary<(string, string), float> Compute(TrackGraph graph, IReadOnlyDictionary<string, List<int>> places,
            CancellationToken cancel)
        {
            var meters = new Dictionary<(string, string), float>();
            var names = places.Keys.Where(p => places[p].Count > 0).ToList();
            for (int i = 0; i < names.Count; i++)
            {
                var start = places[names[i]].SelectMany(t => new[] { TrackGraph.Node(t, true), TrackGraph.Node(t, false) }).ToList();
                var own = new HashSet<int>(places[names[i]]);
                for (int j = i + 1; j < names.Count; j++)
                {
                    var path = graph.FindPath(start, new HashSet<int>(places[names[j]]), ReversePenalty, cancel, own);
                    if (path == null) continue;
                    meters[(names[i], names[j])] = path.Skip(1).Take(path.Count - 2).Sum(step => graph.Tracks[step.Track].Length);
                }
            }
            return meters;
        }
    }
}
