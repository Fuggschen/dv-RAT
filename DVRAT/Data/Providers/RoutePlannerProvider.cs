using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DV.Utils;

namespace DVRAT
{
    /// <summary>Finds the shortest drivable route to a station. Only the search runs on a worker thread.</summary>
    public class RoutePlannerProvider : IDisposable
    {
        /// <summary>Meters a turn at a stop counts as. Above any way round the map, so the train turns only where it must, as in a terminus.</summary>
        private const float TurnAtStopPenalty = 1_000_000f;

        /// <summary>Planning speed for the time estimate, 60 km/h.</summary>
        private const float PlanningSpeedMs = 16.7f;

        public class RouteResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public float TotalDistanceMeters { get; set; }
            public float TotalEstimatedTime { get; set; }
            public List<JunctionAction> JunctionActions { get; set; } = new List<JunctionAction>();
            public int Reversals { get; set; }
            public string DestinationStation { get; set; }

            /// <summary>Null when the route starts where the train stands.</summary>
            public string StartStation { get; set; }

            public bool UnderWire { get; set; }

            /// <summary>Stops on the way in driving order. Empty for a direct route.</summary>
            public List<RouteStop> Stops { get; set; } = new List<RouteStop>();

            /// <summary>One per entry of <see cref="Reversals"/>, in route order.</summary>
            public List<RouteReversal> ReversalPoints { get; set; } = new List<RouteReversal>();
        }

        public class RouteReversal
        {
            public float DistanceFromStart { get; set; }

            /// <summary>How many of <see cref="RouteResult.JunctionActions"/> come before the reversal.</summary>
            public int SwitchIndex { get; set; }
        }

        public class RouteStop
        {
            public string StationId { get; set; }
            public float DistanceFromStart { get; set; }

            /// <summary>How many of <see cref="RouteResult.JunctionActions"/> come before the stop.</summary>
            public int SwitchIndex { get; set; }
        }

        public class JunctionAction
        {
            public string JunctionLabel { get; set; }
            public int RequiredBranchIndex { get; set; }
            public float DistanceFromStart { get; set; }
            public Junction Junction { get; set; }
        }

        private TrackNetwork _network;
        private TrackNetwork _wiredNetwork;
        private HashSet<int> _wiredTracks;
        private float _nextWireSurvey;
        private const float WireSurveyRetrySeconds = 30f;
        private bool _isCalculating;
        private CancellationTokenSource _cancel;

        /// <summary>Kept for the timetable.</summary>
        public RouteResult LastRoute { get; private set; }

        /// <summary>Rebuilt when the game's tracks change. Main thread only.</summary>
        public TrackNetwork Network => GetNetwork();

        /// <summary>Indices of the tracks under the 1500 V DC Catenary mod's wire, or null while there is no wire. Main thread only.</summary>
        public HashSet<int> WiredTracks()
        {
            var network = GetNetwork();
            // Retried because the mod may put the wire up after the world loads.
            if (_wiredNetwork != network && UnityEngine.Time.realtimeSinceStartup >= _nextWireSurvey)
            {
                _wiredTracks = CatenaryWires.Survey(network);
                _wiredNetwork = _wiredTracks != null ? network : null;
                _nextWireSurvey = UnityEngine.Time.realtimeSinceStartup + WireSurveyRetrySeconds;
            }
            return _wiredNetwork == network ? _wiredTracks : null;
        }

        /// <summary>Main thread only.</summary>
        public bool UnderWire(string station)
        {
            var wired = WiredTracks();
            return wired != null && TracksOf(GetNetwork(), station).Any(wired.Contains);
        }

        /// <summary>
        /// Plans from <paramref name="startStation"/>, or from the track of <paramref name="car"/> when null, through each stop in turn.
        /// With <paramref name="underWire"/>, only the track the train stands on may be off the wire, see <see cref="WiredTracks"/>.
        /// The callback runs on the main thread.
        /// </summary>
        public void CalculateRoute(TrainCar car, string startStation, IList<string> stops, string destStation,
            bool underWire, Action<RouteResult> callback)
        {
            if (_isCalculating)
            {
                callback?.Invoke(Fail("A route calculation is already running."));
                return;
            }

            var targets = (stops ?? new List<string>()).Where(s => !string.IsNullOrEmpty(s)).Concat(new[] { destStation }).ToList();
            TrackNetwork network;
            List<int> startNodes;
            List<HashSet<int>> targetTracks;
            HashSet<int> startStationTracks;
            HashSet<int> allowed = null;
            try
            {
                network = GetNetwork();
                startNodes = StartNodes(network, car, startStation);
                targetTracks = targets.Select(t => new HashSet<int>(TracksOf(network, t))).ToList();
                if (underWire)
                {
                    var wired = WiredTracks();
                    if (wired == null)
                    {
                        callback?.Invoke(Fail("The overhead wire could not be read."));
                        return;
                    }
                    // A picked start station is left from its wired tracks. The train's own track may be off the wire.
                    if (startStation != null)
                        startNodes = startNodes.Where(n => wired.Contains(n / 2)).ToList();
                    foreach (var tracks in targetTracks)
                        tracks.IntersectWith(wired);
                    allowed = network.Graph.BridgeGaps(wired, CatenaryLayout.MaxGap);
                    allowed.UnionWith(startNodes.Select(n => n / 2));
                }
                var startTracks = startStation != null ? TracksOf(network, startStation)
                    : network.StationTracks.Values.FirstOrDefault(list => startNodes.Any(n => list.Contains(n / 2)));
                startStationTracks = new HashSet<int>(startTracks ?? new List<int>());
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Route planner could not read the track network: {ex}");
                callback?.Invoke(Fail("Could not read the track network."));
                return;
            }

            string noTracks = underWire ? "has no track under the wire" : "has no tracks";
            if (startNodes.Count == 0)
            {
                string reason = startStation != null ? $"{Stations.Name(startStation)} {noTracks}."
                    : car != null && car.derailed ? "Your train is derailed." : "You are not on a train.";
                callback?.Invoke(Fail(reason));
                return;
            }
            int missing = targetTracks.FindIndex(t => t.Count == 0);
            if (missing >= 0)
            {
                callback?.Invoke(Fail($"{Stations.Name(targets[missing])} {noTracks}."));
                return;
            }
            // Read here because the search runs on a worker thread.
            var names = targets.Select(Stations.Name).ToList();

            _isCalculating = true;
            _cancel?.Dispose();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            var graph = network.Graph;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var legs = new List<List<TrackGraph.Step>>();
                string error = null;
                // Off-wire tracks of the route found without the wire limit, to show where the wire ends.
                List<int> unwired = null;
                try
                {
                    var stopTracks = targetTracks.Cast<ICollection<int>>().ToList();
                    var route = graph.FindRoute(startNodes, stopTracks, startStationTracks, TurnAtStopPenalty, token, allowed);
                    string how = underWire ? "under the wire without reversing" : "without reversing";
                    if (route == null)
                    {
                        error = targets.Count == 1 ? $"No route found to {names[0]} {how}."
                            : $"No route found through {string.Join(", ", names)} {how}.";
                        if (allowed != null)
                            unwired = graph.FindRoute(startNodes, stopTracks, startStationTracks, TurnAtStopPenalty, token)
                                ?.SelectMany(leg => leg).Select(s => s.Track).Where(t => !allowed.Contains(t))
                                .Distinct().ToList();
                    }
                    else
                        legs = route;
                }
                catch (OperationCanceledException)
                {
                    error = "Route calculation cancelled.";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    _isCalculating = false;
                    if (unwired != null && unwired.Count > 0)
                        error += " " + UnwiredText(network, unwired);
                    if (error != null)
                        RATMain.LogError($"Route calculation failed: {error}");

                    var result = error != null ? Fail(error) : ToResult(network, legs, targets);
                    result.StartStation = startStation;
                    result.UnderWire = underWire;
                    if (result.Success)
                        LastRoute = result;
                    callback?.Invoke(result);
                });
            });
        }

        /// <summary>Names the first few off-wire tracks. Asks the wire on the main thread, as the mod's pantographs do.</summary>
        private static string UnwiredText(TrackNetwork network, List<int> unwired)
        {
            const int Shown = 3;
            var parts = unwired.Take(Shown).Select(t =>
            {
                var info = network.Graph.Tracks[t];
                var coverage = CatenaryWires.Coverage(network.Tracks[t]);
                string points = coverage is (int under, int samples) ? $", wire at {under} of {samples} points" : "";
                return $"{info.Name} ({info.Length:F0} m{points})";
            });
            string more = unwired.Count > Shown ? $" and {unwired.Count - Shown} more" : "";
            return $"Off the wire on the way: {string.Join(", ", parts)}{more}.";
        }

        private TrackNetwork GetNetwork()
        {
            int trackCount = SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks.Length;
            if (_network == null || _network.SourceTrackCount != trackCount)
                using (Timing.Measure("Building the track network"))
                    _network = TrackNetwork.Build();
            return _network;
        }

        private static List<int> StartNodes(TrackNetwork network, TrainCar car, string startStation)
        {
            var tracks = new List<int>();
            if (startStation == null)
            {
                var playerTrack = TrackNames.CurrentTrack(car);
                if (playerTrack != null && network.TrackIndex.TryGetValue(playerTrack, out int index))
                    tracks.Add(index);
            }
            else
            {
                tracks.AddRange(TracksOf(network, startStation));
            }

            return tracks.SelectMany(t => new[] { TrackGraph.Node(t, true), TrackGraph.Node(t, false) }).ToList();
        }

        /// <summary>A rural Passenger Jobs station has only its platform on the line. Read on demand because the mod adds stations after world load.</summary>
        private static List<int> TracksOf(TrackNetwork network, string station)
        {
            if (network.StationTracks.TryGetValue(station, out var tracks)) return tracks;
            var rural = RuralStations.All().FirstOrDefault(s => s.Id == station);
            return rural != null && network.TrackIndex.TryGetValue(rural.Track, out int index)
                ? new List<int> { index }
                : new List<int>();
        }

        /// <summary>Main thread only.</summary>
        private static RouteResult ToResult(TrackNetwork network, List<List<TrackGraph.Step>> legs, List<string> targets)
        {
            var result = new RouteResult { Success = true, DestinationStation = targets[targets.Count - 1] };
            float distance = 0f;

            for (int leg = 0; leg < legs.Count; leg++)
            {
                string target = targets[leg];
                // Switches from the first one of the target station on belong to the yard, not the route.
                bool arrived = false;
                float arrival = distance;
                var path = legs[leg];
                // Leaving a stop the other way is a reversal the search did not count.
                if (leg > 0 && path[0].Forward != legs[leg - 1][legs[leg - 1].Count - 1].Forward)
                    AddReversal(result, result.Stops[result.Stops.Count - 1].DistanceFromStart);
                // A later leg starts on the previous leg's last track, already counted.
                for (int i = leg == 0 ? 0 : 1; i < path.Count; i++)
                {
                    var step = path[i];
                    var info = network.Graph.Tracks[step.Track];
                    var junction = step.Junction != TrackGraph.NoJunction ? network.Junctions[step.Junction] : null;
                    if (step.Reverse)
                        AddReversal(result, distance);
                    arrived |= junction != null && string.Equals(junction.StationOf(), target, StringComparison.OrdinalIgnoreCase);
                    if (junction != null && !arrived)
                    {
                        result.JunctionActions.Add(new JunctionAction
                        {
                            Junction = junction,
                            JunctionLabel = junction.Name(),
                            RequiredBranchIndex = step.Branch,
                            DistanceFromStart = distance,
                        });
                    }
                    arrival = distance;
                    distance += info.Length;
                }

                if (leg < legs.Count - 1)
                    result.Stops.Add(new RouteStop
                    {
                        StationId = target,
                        DistanceFromStart = arrival,
                        SwitchIndex = result.JunctionActions.Count,
                    });
            }

            result.TotalDistanceMeters = distance;
            result.TotalEstimatedTime = distance / PlanningSpeedMs;
            return result;
        }

        private static void AddReversal(RouteResult result, float distance)
        {
            result.Reversals++;
            result.ReversalPoints.Add(new RouteReversal { DistanceFromStart = distance, SwitchIndex = result.JunctionActions.Count });
        }

        private static RouteResult Fail(string message) => new RouteResult { Success = false, ErrorMessage = message };

        public void Dispose()
        {
            _cancel?.Cancel();
            _cancel?.Dispose();
        }
    }
}
