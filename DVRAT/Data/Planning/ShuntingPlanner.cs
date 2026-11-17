using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace DVRAT
{
    /// <summary>
    /// Finds the shunting moves that complete the active jobs, fewest moves first. No Unity types.
    /// <para>
    /// The consist is the cars on one side of the loco, from the loco outwards. A move drops the last k cars on a track,
    /// picks up every waiting car on a track, or serves (load, unload, board or alight).
    /// </para>
    /// <para>
    /// A move costs <see cref="MoveCost"/>, plus <see cref="YardChangeCost"/> in another yard, or a trip in another
    /// station: <see cref="StationChangeCost"/> plus <see cref="CostPerMeter"/> per meter when <see cref="RouteCosts"/> knows it.
    /// Yard and station come from track IDs like "HB-C3L" (station HB, yard C) or from <see cref="RouteCosts.Places"/>.
    /// Cars of no active job must be back in the train at the end. Within a station, tracks count as reachable in any order.
    /// Ties go to the plan that hauls the fewest cars.
    /// </para>
    /// </summary>
    public static class ShuntingPlanner
    {
        public const int MoveCost = 10;

        /// <summary>Extra cost of a move in another yard of the same station. Less than a move, so it only breaks ties.</summary>
        public const int YardChangeCost = 1;

        /// <summary>Extra cost of a move in another station. Outweighs up to 100 moves.</summary>
        public const int StationChangeCost = 1000;

        /// <summary>Extra cost per meter of a trip between stations.</summary>
        public const float CostPerMeter = 0.1f;

        public class RouteCosts
        {
            /// <summary>Place of tracks whose ID names no station, e.g. the rural stops of Passenger Jobs.</summary>
            public Dictionary<string, string> Places = new Dictionary<string, string>();

            /// <summary>Meters by rail between two places, either way round. Missing pairs cost a trip without distance.</summary>
            public Dictionary<(string, string), float> Meters = new Dictionary<(string, string), float>();

            /// <summary>Place the train is at. When null, the first move is free wherever it is.</summary>
            public string Start;

            public float Distance(string from, string to) =>
                Meters.TryGetValue((from, to), out float m) || Meters.TryGetValue((to, from), out m) ? m : 0f;

            public (string station, string yard) PlaceOfTrack(string track) =>
                track != null && Places.TryGetValue(track, out string place) ? (place, place) : PlaceOf(track);
        }

        public enum TargetKind
        {
            /// <summary>Uncoupled on the track.</summary>
            Deliver,
            /// <summary>On the warehouse track, coupled or not.</summary>
            Serve,
            /// <summary>At a passenger stop, coupled to the train.</summary>
            Board,
            /// <summary>Uncoupled at the last stop of its passenger job.</summary>
            Leave,
        }

        public readonly struct Target
        {
            public Target(TargetKind kind, string track)
            {
                Kind = kind;
                Track = track;
            }

            public TargetKind Kind { get; }
            public string Track { get; }
        }

        public class Car
        {
            public string Id;

            /// <summary>Remaining targets in order. Empty for cars of no active job.</summary>
            public List<Target> Targets = new List<Target>();

            /// <summary>Length in meters including the gap to the next car; 0 when unknown.</summary>
            public float Length;
        }

        public enum MoveKind { Drop, PickUp, Serve }

        public class Move
        {
            public MoveKind Kind;
            public string Track;

            /// <summary>A serve or drop at a Passenger Jobs stop. A rural stop is a stretch of a main line track, not a track of its own.</summary>
            public bool Passengers;

            /// <summary>Car IDs the move handles, from the loco side outwards.</summary>
            public List<string> Cars = new List<string>();
        }

        public class Plan
        {
            public List<Move> Moves = new List<Move>();

            /// <summary>True when the search finished, so no plan with fewer moves exists.</summary>
            public bool ProvenOptimal;

            /// <summary>Set when no plan exists.</summary>
            public string Error;

            /// <summary>Things that stop the plan from working as shown, e.g. a destination track too full.</summary>
            public List<string> Warnings = new List<string>();
        }

        /// <param name="waiting">Cars standing on tracks, by track, in coupling order.</param>
        /// <param name="maxNodes">Search states to store before falling back to the greedy plan.</param>
        /// <param name="progress">Receives 0 to 1 while searching, from the worker thread.</param>
        /// <param name="trackSpace">Meters per track the planned cars may use. Missing tracks have no limit.</param>
        public static Plan Solve(IReadOnlyList<string> consist, IReadOnlyDictionary<string, List<string>> waiting,
            IReadOnlyList<Car> cars, int maxNodes, Action<float> progress, CancellationToken cancel,
            IReadOnlyDictionary<string, float> trackSpace = null, RouteCosts routes = null)
        {
            var problem = new Problem(consist, waiting, cars, trackSpace, routes ?? new RouteCosts());
            if (problem.Error != null) return new Plan { Error = problem.Error };

            var best = problem.Search(maxNodes, progress, cancel);
            if (best != null) return new Plan { Moves = best, ProvenOptimal = true, Warnings = problem.Warnings };

            var greedy = problem.Greedy();
            return greedy != null
                ? new Plan { Moves = greedy, Warnings = problem.Warnings }
                : new Plan { Error = "No plan found within the search limit.", Warnings = problem.Warnings };
        }

        /// <summary>Cars as small integers, so states hash and copy cheaply.</summary>
        private class Problem
        {
            private readonly string[] _carIds;
            private readonly Target[][] _targets;
            private readonly float[] _length;
            private readonly float[] _space;
            private readonly string[] _tracks;
            private readonly Dictionary<string, int> _trackIndex = new Dictionary<string, int>();
            private readonly int[] _yardOf;
            private readonly int[] _stationOf;
            private readonly int[,] _trip;
            // For the lower bound.
            private readonly int[] _cheapestEntry;
            // -1 when unknown.
            private readonly int _startStation;
            // Passenger stops, where only cars of a job ending there are dropped.
            private readonly bool[] _stop;
            private readonly State _start;
            public readonly string Error;
            public readonly List<string> Warnings = new List<string>();

            public Problem(IReadOnlyList<string> consist, IReadOnlyDictionary<string, List<string>> waiting,
                IReadOnlyList<Car> cars, IReadOnlyDictionary<string, float> trackSpace, RouteCosts routes)
            {
                _carIds = cars.Select(c => c.Id).ToArray();
                _targets = cars.Select(c => c.Targets.ToArray()).ToArray();
                _length = cars.Select(c => c.Length).ToArray();
                var carIndex = new Dictionary<string, int>();
                for (int i = 0; i < _carIds.Length; i++)
                    carIndex[_carIds[i]] = i;

                var tracks = new List<string>();
                void AddTrack(string t)
                {
                    if (t != null && !_trackIndex.ContainsKey(t))
                    {
                        _trackIndex[t] = tracks.Count;
                        tracks.Add(t);
                    }
                }
                foreach (var car in cars)
                    foreach (var target in car.Targets)
                        AddTrack(target.Track);
                foreach (var track in waiting.Keys)
                    AddTrack(track);
                _tracks = tracks.ToArray();
                var yards = new Dictionary<string, int>();
                var stations = new Dictionary<string, int>();
                int Id(Dictionary<string, int> ids, string name) => ids.TryGetValue(name, out int id) ? id : ids[name] = ids.Count;
                _yardOf = _tracks.Select(t => Id(yards, routes.PlaceOfTrack(t).yard)).ToArray();
                _stationOf = _tracks.Select(t => Id(stations, routes.PlaceOfTrack(t).station)).ToArray();
                _startStation = routes.Start != null ? Id(stations, routes.Start) : -1;
                var names = stations.OrderBy(s => s.Value).Select(s => s.Key).ToArray();
                _trip = new int[names.Length, names.Length];
                _cheapestEntry = new int[names.Length];
                for (int to = 0; to < names.Length; to++)
                {
                    _cheapestEntry[to] = int.MaxValue;
                    for (int from = 0; from < names.Length; from++)
                    {
                        if (from == to) continue;
                        _trip[from, to] = StationChangeCost + (int)(routes.Distance(names[from], names[to]) * CostPerMeter);
                        _cheapestEntry[to] = Math.Min(_cheapestEntry[to], _trip[from, to]);
                    }
                    if (_cheapestEntry[to] == int.MaxValue)
                        _cheapestEntry[to] = StationChangeCost;
                }
                _space = _tracks.Select(t => trackSpace != null && trackSpace.TryGetValue(t, out float m) ? m : float.PositiveInfinity).ToArray();
                _stop = _tracks.Select(t => _targets.Any(list => list.Any(target =>
                    (target.Kind == TargetKind.Board || target.Kind == TargetKind.Leave) && target.Track == t))).ToArray();

                var start = new State
                {
                    Consist = consist.Where(carIndex.ContainsKey).Select(id => carIndex[id]).ToList(),
                    OnTrack = _tracks.Select(_ => new List<int>()).ToArray(),
                    Progress = new int[_carIds.Length],
                    Location = -1,
                };
                foreach (var pair in waiting)
                    foreach (string id in pair.Value)
                        if (carIndex.TryGetValue(id, out int car) && _targets[car].Length > 0)
                            start.OnTrack[_trackIndex[pair.Key]].Add(car);

                // Cars already on their warehouse or destination track count as served.
                for (int t = 0; t < _tracks.Length; t++)
                    foreach (int car in start.OnTrack[t])
                        start.AdvanceOnTrack(car, _tracks[t], _targets);
                _start = start;

                var stuck = Enumerable.Range(0, _carIds.Length).Where(c => !start.Consist.Contains(c) && _targets[c].Length > 0
                    && start.OnTrack.All(list => !list.Contains(c))).ToList();
                if (stuck.Count > 0)
                    Error = $"Cars not in the train and not on a known track: {string.Join(", ", stuck.Select(c => _carIds[c]))}";
                else
                    AllowFullDestinations();
            }

            /// <summary>Lifts the length limit of destination tracks too full for their deliveries and warns about it.</summary>
            private void AllowFullDestinations()
            {
                var needed = new float[_tracks.Length];
                for (int car = 0; car < _carIds.Length; car++)
                {
                    var targets = _targets[car];
                    if (targets.Length > 0 && targets[targets.Length - 1].Kind == TargetKind.Deliver)
                        needed[_trackIndex[targets[targets.Length - 1].Track]] += _length[car];
                }
                for (int t = 0; t < _tracks.Length; t++)
                {
                    if (needed[t] <= _space[t]) continue;
                    Warnings.Add($"{_tracks[t]} has room for {Math.Max(0f, _space[t]):F0} m, the cars to deliver there need " +
                        $"{needed[t]:F0} m. Other cars stand on it.");
                    _space[t] = float.PositiveInfinity;
                }
            }

            /// <summary>A* over move sequences, or null at the node limit. Ties on cost go to fewer hauled cars.</summary>
            public List<Move> Search(int maxNodes, Action<float> progress, CancellationToken cancel)
            {
                var open = new SortedSet<(int f, int hauled, int order)>();
                var nodes = new Dictionary<int, (State state, int cost, int hauled, int parent, Move move)>();
                var seen = new Dictionary<string, int>();
                int order = 0;

                nodes[0] = (_start, 0, 0, -1, null);
                open.Add((Heuristic(_start), 0, 0));
                seen[_start.Key(_yardOf)] = 0;

                while (open.Count > 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    var (_, _, id) = open.Min;
                    open.Remove(open.Min);
                    var (state, cost, hauled, parent, move) = nodes[id];

                    if (state.IsGoal(_targets))
                        return Path(nodes, id);

                    // Only the path back is needed now. Dropping the state bounds memory.
                    nodes[id] = (null, cost, hauled, parent, move);

                    foreach (var (nextMove, next) in Successors(state))
                    {
                        string key = next.Key(_yardOf);
                        int nextCost = cost + MoveCost + ChangeCost(state.Location, next.Location);
                        int nextHauled = hauled + next.Consist.Count;
                        if (seen.TryGetValue(key, out int known))
                        {
                            var k = nodes[known];
                            if (k.cost < nextCost || (k.cost == nextCost && k.hauled <= nextHauled)) continue;
                        }

                        // The cap counts stored states, not expansions.
                        if (nodes.Count >= maxNodes) return null;
                        int nextId = ++order;
                        nodes[nextId] = (next, nextCost, nextHauled, id, nextMove);
                        seen[key] = nextId;
                        open.Add((nextCost + Heuristic(next), nextHauled, nextId));
                    }
                    if (order % 2000 < 64) progress?.Invoke((float)nodes.Count / maxNodes);
                }
                return null;
            }

            /// <summary>Extra cost of moving from <paramref name="from"/> (-1 for the start) to <paramref name="to"/>.</summary>
            private int ChangeCost(int from, int to)
            {
                if (to < 0) return 0;
                int station = from >= 0 ? _stationOf[from] : _startStation;
                if (station < 0) return 0;
                if (station != _stationOf[to]) return _trip[station, _stationOf[to]];
                return from >= 0 && _yardOf[from] != _yardOf[to] ? YardChangeCost : 0;
            }

            /// <summary>
            /// Lower bound: each track with a remaining drop, serve or pick-up needs one move. Counting all remaining
            /// targets, not just the next, finishes the search far sooner.
            /// </summary>
            private int Heuristic(State state)
            {
                var tracks = new HashSet<int>();
                for (int car = 0; car < _carIds.Length; car++)
                    for (int p = state.Progress[car]; p < _targets[car].Length; p++)
                        tracks.Add(_trackIndex[_targets[car][p].Track]);
                for (int t = 0; t < state.OnTrack.Length; t++)
                {
                    if (state.OnTrack[t].Any(car => state.WantsTrain(car, _targets)))
                        tracks.Add(t);
                }

                int here = state.Location;
                int hereStation = here >= 0 ? _stationOf[here] : _startStation;
                int changes = 0;
                foreach (var station in tracks.GroupBy(t => _stationOf[t]))
                {
                    int yards = station.Select(t => _yardOf[t]).Distinct().Count();
                    if (here >= 0 && station.Key == hereStation)
                        changes += YardChangeCost * (yards - (station.Any(t => _yardOf[t] == _yardOf[here]) ? 1 : 0));
                    else
                        changes += (hereStation >= 0 && station.Key != hereStation ? _cheapestEntry[station.Key] : 0)
                            + YardChangeCost * (yards - 1);
                }
                return tracks.Count * MoveCost + changes;
            }

            private float Used(State state, int track) => state.OnTrack[track].Sum(car => _length[car]);

            private bool Fits(State state, int track, int count)
            {
                if (float.IsPositiveInfinity(_space[track])) return true;
                float block = 0f;
                for (int i = state.Consist.Count - count; i < state.Consist.Count; i++)
                    block += _length[state.Consist[i]];
                return Used(state, track) + block <= _space[track];
            }

            /// <summary>Due cars served at <paramref name="track"/>, from the train end inwards, as many as fit.</summary>
            private List<int> Servable(State state, int track)
            {
                var served = new List<int>();
                float room = _space[track] - Used(state, track);
                for (int i = state.Consist.Count - 1; i >= 0; i--)
                {
                    int car = state.Consist[i];
                    if (!NextIs(state, car, TargetKind.Serve, track) && !NextIs(state, car, TargetKind.Board, track)) continue;
                    if (_length[car] > room) break;
                    room -= _length[car];
                    served.Add(car);
                }
                served.Reverse();
                return served;
            }

            /// <summary>
            /// Moves worth trying. A block is only dropped where its first car (nearest the loco) or the car in front of it is due.
            /// Other drops never shorten a plan in DV job layouts, and trying them all used gigabytes.
            /// </summary>
            private IEnumerable<(Move, State)> Successors(State state)
            {
                int length = state.Consist.Count;
                for (int t = 0; t < _tracks.Length; t++)
                {
                    if (_stop[t])
                    {
                        // At a passenger stop only cars whose job ends there are left.
                        int ending = 0;
                        while (ending < length && NextIs(state, state.Consist[length - 1 - ending], TargetKind.Leave, t))
                            ending++;
                        if (ending > 0 && Fits(state, t, ending))
                            yield return Drop(state, t, ending);
                    }
                    else
                    {
                        for (int k = 1; k <= length; k++)
                        {
                            int first = state.Consist[length - k];
                            int before = k < length ? state.Consist[length - k - 1] : -1;
                            if ((NextTrack(state, first) == t || (before >= 0 && NextTrack(state, before) == t)) && Fits(state, t, k))
                                yield return Drop(state, t, k);
                        }
                    }

                    if (state.OnTrack[t].Any(car => state.WantsTrain(car, _targets)))
                        yield return PickUp(state, t);

                    if (Servable(state, t).Count > 0)
                        yield return Serve(state, t);
                }
            }

            /// <summary>-1 when the car has no next target.</summary>
            private int NextTrack(State state, int car)
            {
                int p = state.Progress[car];
                return p < _targets[car].Length ? _trackIndex[_targets[car][p].Track] : -1;
            }

            private bool Leaves(State state, int car, int track) =>
                NextIs(state, car, TargetKind.Deliver, track) || NextIs(state, car, TargetKind.Leave, track);

            private bool NextIs(State state, int car, TargetKind kind, int track)
            {
                int p = state.Progress[car];
                return p < _targets[car].Length && _targets[car][p].Kind == kind && _trackIndex[_targets[car][p].Track] == track;
            }

            private (Move, State) Drop(State state, int track, int count)
            {
                var next = state.Copy();
                var block = next.Consist.GetRange(next.Consist.Count - count, count);
                next.Consist.RemoveRange(next.Consist.Count - count, count);
                next.OnTrack[track].AddRange(block);
                foreach (int car in block)
                    next.AdvanceOnTrack(car, _tracks[track], _targets);
                next.Location = track;
                return (MakeMove(MoveKind.Drop, track, block), next);
            }

            private (Move, State) PickUp(State state, int track)
            {
                var next = state.Copy();
                // Delivered cars stay where they are.
                var block = next.OnTrack[track].Where(car => next.WantsTrain(car, _targets)).ToList();
                next.OnTrack[track].RemoveAll(block.Contains);
                next.Consist.AddRange(block);
                next.Location = track;
                return (MakeMove(MoveKind.PickUp, track, block), next);
            }

            private (Move, State) Serve(State state, int track)
            {
                var next = state.Copy();
                var served = Servable(next, track);
                foreach (int car in served)
                    next.Progress[car]++;
                next.Location = track;
                return (MakeMove(MoveKind.Serve, track, served), next);
            }

            private Move MakeMove(MoveKind kind, int track, List<int> cars) =>
                new Move
                {
                    Kind = kind,
                    Track = _tracks[track],
                    Cars = cars.Select(c => _carIds[c]).ToList(),
                    Passengers = kind != MoveKind.PickUp && _stop[track],
                };

            private static List<Move> Path(Dictionary<int, (State state, int cost, int hauled, int parent, Move move)> nodes, int id)
            {
                var moves = new List<Move>();
                for (int n = id; nodes[n].parent >= 0; n = nodes[n].parent)
                    moves.Add(nodes[n].move);
                moves.Reverse();
                return moves;
            }

            /// <summary>Fallback at the search limit. Collects every waiting car, then serves, drops, and sets aside end cars in the way.</summary>
            public List<Move> Greedy()
            {
                var state = _start.Copy();
                var moves = new List<Move>();
                for (int guard = 0; guard < 500 && !state.IsGoal(_targets); guard++)
                {
                    var step = NextGreedyStep(state);
                    if (step == null) return null;
                    moves.Add(step.Value.move);
                    state = step.Value.next;
                }
                return state.IsGoal(_targets) ? moves : null;
            }

            private (Move move, State next)? NextGreedyStep(State state)
            {
                // Nearest first.
                var byDistance = Enumerable.Range(0, _tracks.Length).OrderBy(t => ChangeCost(state.Location, t)).ToList();
                foreach (int t in byDistance)
                    if (state.OnTrack[t].Any(car => state.WantsTrain(car, _targets)))
                        return PickUp(state, t);

                foreach (int t in byDistance)
                    if (Servable(state, t).Count > 0)
                        return Serve(state, t);

                if (state.Consist.Count == 0) return null;
                int length = state.Consist.Count;
                int tail = state.Consist[length - 1];
                int track = NextTrack(state, tail);
                if (track >= 0 && Leaves(state, tail, track))
                {
                    int count = 1;
                    while (count < length && Leaves(state, state.Consist[length - 1 - count], track))
                        count++;
                    while (count > 0 && !Fits(state, track, count))
                        count--;
                    if (count > 0)
                        return Drop(state, track, count);
                }

                // End cars are not due here. Set them aside where the car in front is due.
                int due = state.Consist.FindLastIndex(car => NextTrack(state, car) >= 0 && car != tail);
                if (due < 0) return null;
                int aside = length - 1 - due;
                int place = NextTrack(state, state.Consist[due]);
                return !_stop[place] && Fits(state, place, aside) ? Drop(state, place, aside) : ((Move, State)?)null;
            }
        }

        private class State
        {
            public List<int> Consist;
            public List<int>[] OnTrack;

            /// <summary>Index of the next target per car.</summary>
            public int[] Progress;

            /// <summary>-1 before the first move.</summary>
            public int Location;

            public State Copy() => new State
            {
                Consist = new List<int>(Consist),
                OnTrack = OnTrack.Select(l => new List<int>(l)).ToArray(),
                Progress = (int[])Progress.Clone(),
                Location = Location,
            };

            public bool IsFinished(int car, Target[][] targets) => Progress[car] >= targets[car].Length;

            /// <summary>Unfinished job cars, and cars of no job that were only set aside.</summary>
            public bool WantsTrain(int car, Target[][] targets) => targets[car].Length == 0 || !IsFinished(car, targets);

            public void AdvanceOnTrack(int car, string track, Target[][] targets)
            {
                while (Progress[car] < targets[car].Length && targets[car][Progress[car]].Track == track)
                    Progress[car]++;
            }

            public bool IsGoal(Target[][] targets)
            {
                for (int car = 0; car < Progress.Length; car++)
                    if (Progress[car] < targets[car].Length) return false;
                foreach (var list in OnTrack)
                    foreach (int car in list)
                        if (targets[car].Length == 0) return false;
                return true;
            }

            /// <summary>Only the location's yard matters, so states in one yard merge.</summary>
            public string Key(int[] yardOf)
            {
                var key = new StringBuilder();
                key.Append(Location < 0 ? -1 : yardOf[Location]).Append('@');
                key.Append(string.Join(",", Consist)).Append('|');
                foreach (var list in OnTrack)
                    // Order decides the coupling order.
                    key.Append(string.Join(",", list)).Append(';');
                key.Append('|').Append(string.Join(",", Progress));
                return key.ToString();
            }
        }

        /// <summary>Station and yard of a track ID: "HB-C3L" is station "HB", yard "HB-C". IDs without a station all count as one place.</summary>
        public static (string station, string yard) PlaceOf(string track)
        {
            int dash = track?.IndexOf('-') ?? -1;
            if (dash <= 0) return ("", "");
            string station = track.Substring(0, dash);
            int end = dash + 1;
            while (end < track.Length && char.IsLetter(track[end])) end++;
            return (station, track.Substring(0, end));
        }

        /// <summary>
        /// The stations a plan drives to, for the route planner. The start is null when the train is at none of <paramref name="stations"/>.
        /// Null when the plan stays at the start.
        /// </summary>
        public static (string start, List<string> stops, string destination)? RouteOf(IEnumerable<Move> moves, RouteCosts routes,
            ICollection<string> stations)
        {
            string start = routes?.Start != null && stations.Contains(routes.Start) ? routes.Start : null;
            var visits = new List<string>();
            foreach (var move in moves)
            {
                string station = (routes ?? new RouteCosts()).PlaceOfTrack(move.Track).station;
                if (!stations.Contains(station)) continue;
                if (visits.Count == 0 ? station == start || station == routes?.Start : visits[visits.Count - 1] == station) continue;
                visits.Add(station);
            }
            if (visits.Count == 0) return null;
            return (start, visits.Take(visits.Count - 1).ToList(), visits[visits.Count - 1]);
        }

        public static List<Target> TargetsFromLegs(IReadOnlyList<JobLeg> legs)
        {
            var targets = new List<Target>();
            for (int i = 0; i < legs.Count; i++)
            {
                var leg = legs[i];
                if (leg.Kind == JobLegKind.Transport)
                {
                    // A move to the warehouse is covered by being served there.
                    bool servedNext = i + 1 < legs.Count && legs[i + 1].Kind != JobLegKind.Transport && legs[i + 1].ToTrack == leg.ToTrack;
                    if (!servedNext)
                        targets.Add(new Target(TargetKind.Deliver, leg.ToTrack));
                }
                else if (leg.Passengers)
                {
                    // Passengers alight and board in one stop. The last stop ends the job and the cars are left there.
                    bool sameStop = targets.Count > 0 && targets[targets.Count - 1].Kind == TargetKind.Board
                        && targets[targets.Count - 1].Track == leg.ToTrack;
                    var kind = i == legs.Count - 1 ? TargetKind.Leave : TargetKind.Board;
                    if (sameStop)
                        targets[targets.Count - 1] = new Target(kind, leg.ToTrack);
                    else
                        targets.Add(new Target(kind, leg.ToTrack));
                }
                else
                {
                    targets.Add(new Target(TargetKind.Serve, leg.ToTrack));
                }
            }
            return targets;
        }
    }
}
