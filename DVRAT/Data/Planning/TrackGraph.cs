using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DVRAT
{
    /// <summary>Track network as plain data, safe to search on a worker thread. A node is a track plus a travel direction.</summary>
    public class TrackGraph
    {
        public const int NoJunction = -1;

        public class TrackInfo
        {
            public string Name;
            public float Length;
            public float GradePercent;
            /// <summary>A station's storage, loading, parking or in/out track. Routes end on one but never pass it.</summary>
            public bool Yard;
        }

        public readonly struct Edge
        {
            public Edge(int to, int junction, int branch)
            {
                To = to;
                Junction = junction;
                Branch = branch;
            }

            public int To { get; }

            /// <summary>Junction passed facing, or <see cref="NoJunction"/>.</summary>
            public int Junction { get; }

            public int Branch { get; }
        }

        public class Step
        {
            public int Track;
            public bool Forward;
            public int Junction = NoJunction;
            public int Branch;
            /// <summary>The train changes direction before entering this track.</summary>
            public bool Reverse;
        }

        private readonly List<TrackInfo> _tracks = new List<TrackInfo>();
        private readonly List<List<Edge>> _edges = new List<List<Edge>>();

        public IReadOnlyList<TrackInfo> Tracks => _tracks;

        public static int Node(int track, bool forward) => track * 2 + (forward ? 1 : 0);

        public int AddTrack(string name, float length, float gradePercent, bool yard = false)
        {
            _tracks.Add(new TrackInfo { Name = name, Length = length, GradePercent = gradePercent, Yard = yard });
            _edges.Add(new List<Edge>());
            _edges.Add(new List<Edge>());
            return _tracks.Count - 1;
        }

        public void Connect(int fromTrack, bool fromForward, int toTrack, bool toForward, int junction = NoJunction, int branch = 0)
        {
            _edges[Node(fromTrack, fromForward)].Add(new Edge(Node(toTrack, toForward), junction, branch));
        }

        /// <summary><paramref name="tracks"/> plus each track up to <paramref name="maxGap"/> m long that joins one of them at both ends.</summary>
        public HashSet<int> BridgeGaps(ICollection<int> tracks, float maxGap)
        {
            bool Joins(int node) => _edges[node].Any(e => tracks.Contains(e.To / 2));
            var bridged = new HashSet<int>(tracks);
            for (int t = 0; t < _tracks.Count; t++)
                if (_tracks[t].Length <= maxGap && !tracks.Contains(t) && Joins(Node(t, true)) && Joins(Node(t, false)))
                    bridged.Add(t);
            return bridged;
        }

        /// <summary>
        /// Cheapest way from any start node onto any destination track, or null. A reversal costs <paramref name="reversePenalty"/> meters.
        /// Yard tracks are only entered when a destination or in <paramref name="passable"/>.
        /// </summary>
        public List<Step> FindPath(IEnumerable<int> startNodes, ICollection<int> destinationTracks, float reversePenalty,
            CancellationToken cancel, ICollection<int> passable = null)
        {
            int nodeCount = _tracks.Count * 2;
            var cost = new float[nodeCount];
            var previous = new int[nodeCount];
            var via = new Edge[nodeCount];
            var reversed = new bool[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                cost[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            var queue = new SortedSet<(float cost, int node)>();
            foreach (int start in startNodes)
            {
                cost[start] = 0;
                queue.Add((0, start));
            }

            while (queue.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                var (currentCost, node) = queue.Min;
                queue.Remove(queue.Min);
                if (currentCost > cost[node]) continue;

                if (destinationTracks.Contains(node / 2))
                    return BuildPath(node, previous, via, reversed);

                float leaveCost = currentCost + _tracks[node / 2].Length;
                foreach (var edge in _edges[node])
                {
                    int to = edge.To / 2;
                    if (_tracks[to].Yard && !destinationTracks.Contains(to) && (passable == null || !passable.Contains(to))) continue;
                    Relax(queue, cost, previous, via, reversed, node, edge, leaveCost, false);
                }

                Relax(queue, cost, previous, via, reversed, node, new Edge(node ^ 1, NoJunction, 0), currentCost + reversePenalty, true);
            }
            return null;
        }

        /// <summary>
        /// Cheapest way through any track of each stop in turn, one leg per stop, or null. The train never turns on the line.
        /// A turn at a stop costs <paramref name="turnAtStopPenalty"/> meters. Each leg may cross the yard tracks of the place it leaves.
        /// With <paramref name="allowed"/>, no other track is entered.
        /// </summary>
        public List<List<Step>> FindRoute(IEnumerable<int> startNodes, IReadOnlyList<ICollection<int>> stops,
            ICollection<int> startTracks, float turnAtStopPenalty, CancellationToken cancel, ICollection<int> allowed = null)
        {
            int nodes = _tracks.Count * 2;
            int states = nodes * stops.Count;
            var cost = new float[states];
            var previous = new int[states];
            var via = new Edge[states];
            for (int i = 0; i < states; i++)
            {
                cost[i] = float.PositiveInfinity;
                previous[i] = -1;
            }

            var queue = new SortedSet<(float cost, int state)>();
            void Visit(int from, int to, Edge edge, float newCost)
            {
                if (newCost >= cost[to]) return;
                queue.Remove((cost[to], to));
                cost[to] = newCost;
                previous[to] = from;
                via[to] = edge;
                queue.Add((newCost, to));
            }
            foreach (int start in startNodes)
                Visit(-1, start, new Edge(start, NoJunction, 0), 0f);

            while (queue.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                var (currentCost, state) = queue.Min;
                queue.Remove(queue.Min);
                if (currentCost > cost[state]) continue;
                int leg = state / nodes, node = state % nodes, track = node / 2;

                if (stops[leg].Contains(track))
                {
                    if (leg == stops.Count - 1)
                        return BuildRoute(state, nodes, previous, via);
                    int next = (leg + 1) * nodes;
                    Visit(state, next + node, new Edge(node, NoJunction, 0), currentCost);
                    Visit(state, next + (node ^ 1), new Edge(node ^ 1, NoJunction, 0), currentCost + turnAtStopPenalty);
                }

                var leaving = leg == 0 ? startTracks : stops[leg - 1];
                float leaveCost = currentCost + _tracks[track].Length;
                foreach (var edge in _edges[node])
                {
                    int to = edge.To / 2;
                    if (_tracks[to].Yard && !stops[leg].Contains(to) && (leaving == null || !leaving.Contains(to))) continue;
                    if (allowed != null && !allowed.Contains(to)) continue;
                    Visit(state, leg * nodes + edge.To, edge, leaveCost);
                }
            }
            return null;
        }

        private static List<List<Step>> BuildRoute(int end, int nodes, int[] previous, Edge[] via)
        {
            var chain = new List<int>();
            for (int state = end; state >= 0; state = previous[state])
                chain.Add(state);
            chain.Reverse();

            var legs = new List<List<Step>>();
            int legOf = -1;
            foreach (int state in chain)
            {
                int leg = state / nodes, node = state % nodes;
                if (leg != legOf)
                {
                    legs.Add(new List<Step>());
                    legOf = leg;
                }
                var step = new Step { Track = node / 2, Forward = (node & 1) == 1 };
                if (previous[state] >= 0 && previous[state] / nodes == leg)
                {
                    step.Junction = via[state].Junction;
                    step.Branch = via[state].Branch;
                }
                legs[legs.Count - 1].Add(step);
            }
            return legs;
        }

        private static void Relax(SortedSet<(float, int)> queue, float[] cost, int[] previous, Edge[] via, bool[] reversed,
            int from, Edge edge, float newCost, bool isReversal)
        {
            if (newCost >= cost[edge.To]) return;

            queue.Remove((cost[edge.To], edge.To));
            cost[edge.To] = newCost;
            previous[edge.To] = from;
            via[edge.To] = edge;
            reversed[edge.To] = isReversal;
            queue.Add((newCost, edge.To));
        }

        private static List<Step> BuildPath(int end, int[] previous, Edge[] via, bool[] reversed)
        {
            var steps = new List<Step>();
            bool reverseNext = false;
            for (int node = end; node >= 0; node = previous[node])
            {
                if (reversed[node])
                {
                    // A reversal has no step of its own. Mark the step after it.
                    reverseNext = true;
                    continue;
                }

                var step = new Step { Track = node / 2, Forward = (node & 1) == 1 };
                if (previous[node] >= 0)
                {
                    step.Junction = via[node].Junction;
                    step.Branch = via[node].Branch;
                }
                if (reverseNext && steps.Count > 0)
                    steps[steps.Count - 1].Reverse = true;
                reverseNext = false;
                steps.Add(step);
            }
            steps.Reverse();
            return steps;
        }
    }
}
