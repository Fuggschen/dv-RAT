using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Scans the track ahead of the player's car along the current switch settings. Limits and grades are the game's signs, see <see cref="TrackSigns"/>.</summary>
    public class RouteDataProvider : IDisposable
    {
        public const float ScanRangeMeters = 10000f;

        private const int MaxTracks = 1000;

        /// <summary>A car that moved farther between two scans was teleported.</summary>
        private const float MaxMoveMeters = 500f;

        public const int PassedEventCount = 5;

        // Resets when the car or direction changes.
        private readonly SpeedHistory _history = new SpeedHistory();
        private readonly EventHistory _events = new EventHistory();
        private float _odometer;
        private Vector3? _lastPosition;
        private TrainCar _lastCar;
        private bool _lastMovesForward;

        /// <param name="switchDamage">Read switch damage from the Junction Maintenance mod.</param>
        /// <param name="trackDamage">Lower the limits where the Track Maintenance mod has damaged track.</param>
        public RouteData Refresh(TrainCar car, RoutePlannerProvider.RouteResult plannedRoute, bool switchDamage, bool trackDamage)
        {
            var data = new RouteData { PlannedDestination = plannedRoute?.DestinationStation };
            try
            {
                if (!Locate(car, out var track, out bool forward, out float startSpan, out bool carMovesForward)) return data;

                data.CurrentTrackId = track.LogicTrack()?.ID.FullID ?? track.gameObject.name;
                data.CurrentTrackName = track.LogicTrack()?.ID.FullDisplayID ?? data.CurrentTrackId;
                data.Direction = carMovesForward ? "forward" : "backward";

                var speeds = new List<(float at, float value)>();
                var grades = new List<(float at, float value)>();
                Scan(data, speeds, grades, track, forward, startSpan, switchDamage, trackDamage);
                MarkMisaligned(data, plannedRoute);

                data.SpeedSections = SpeedRestrictions.Apply(RouteProfile.Sections(speeds, data.ScannedMeters),
                    data.Restrictions, data.ScannedMeters);
                data.GradeSections = RouteProfile.Sections(grades, data.ScannedMeters);
                data.SpeedLimitKmh = RouteProfile.At(data.SpeedSections, 0)?.Value ?? 0;
                data.CurrentGradePercent = RouteProfile.At(data.GradeSections, 0)?.Value ?? 0;
                AddSectionEvents(data);

                data.UpcomingEvents.Sort((a, b) => a.DistanceMeters.CompareTo(b.DistanceMeters));
                Remember(car, carMovesForward, data);
                var next = data.UpcomingEvents.FirstOrDefault(e => e.EventType == TrackEventType.Station && e.DistanceMeters > 0);
                if (next != null)
                {
                    data.NextStationName = next.StationId;
                    data.DistanceToNextStation = next.DistanceMeters;
                }
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Route scan failed: {ex}");
            }
            return data;
        }

        /// <summary>Marks switches set against the planned route. One the route takes twice to different branches stays unmarked.</summary>
        private static void MarkMisaligned(RouteData data, RoutePlannerProvider.RouteResult plannedRoute)
        {
            if (plannedRoute?.JunctionActions == null) return;
            var required = plannedRoute.JunctionActions
                .Where(a => a.JunctionLabel != null)
                .GroupBy(a => a.JunctionLabel)
                .Where(g => g.Select(a => a.RequiredBranchIndex).Distinct().Count() == 1)
                .ToDictionary(g => g.Key, g => g.First().RequiredBranchIndex);
            // A switch run through from a branch needs no setting.
            foreach (var ev in data.UpcomingEvents.Where(e => e.EventType == TrackEventType.Junction && !e.Trailing))
                ev.Misaligned = required.TryGetValue(ev.Name, out int branch) && branch != (int)ev.Value;
        }

        /// <summary>Finds the track under the car and whether the train runs along it from its in to its out end. False when on no track.</summary>
        private static bool Locate(TrainCar car, out RailTrack track, out bool forward, out float startSpan, out bool carMovesForward)
        {
            track = TrackNames.CurrentTrack(car);
            forward = true;
            startSpan = 0f;
            carMovesForward = true;
            if (track == null) return false;

            var points = track.GetKinkedPointSet();
            // Point sets use unshifted world coordinates. Transforms move with the floating origin.
            var start = ClosestPoint(points, car.transform.position - DV.OriginShift.OriginShift.currentMove);
            bool carAlongTrack = Vector3.Dot(car.transform.forward, start.forward) >= 0;
            carMovesForward = TrackNames.MovesForward(car);
            forward = carAlongTrack == carMovesForward;
            startSpan = (float)start.span;
            return true;
        }

        /// <summary>
        /// Builds the signs ahead before the first read, because building all at once stalls the game for most of a second.
        /// Each call builds for about <paramref name="budgetMs"/>, at least one track or junction. Returns true once the scan range is built.
        /// </summary>
        public bool WarmUp(TrainCar car, double budgetMs)
        {
            try
            {
                if (!Locate(car, out var track, out bool forward, out _, out _)) return true;

                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                bool builtOne = false;
                bool MayBuild()
                {
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (builtOne && ms >= budgetMs) return false;
                    builtOne = true;
                    return true;
                }
                bool Arcs(RailTrack t)
                {
                    if (t == null || TrackSigns.HasArcs(t.curve)) return true;
                    if (!MayBuild()) return false;
                    TrackSigns.BuildArcs(t.curve);
                    return true;
                }
                bool Track(RailTrack t, bool f)
                {
                    if (TrackSigns.IsBuilt(t, f)) return true;
                    // The arcs are the slow part. The next track's first segment caps this track's last sign.
                    // Separate steps keep a long track from taking one long frame.
                    if (!Arcs(t) || !Arcs((f ? t.GetOutBranch() : t.GetInBranch())?.track)) return false;
                    if (!MayBuild()) return false;
                    TrackSigns.For(t, f);
                    return true;
                }

                // The scan also reads the last signs of the track behind.
                var behind = forward ? track.GetInBranch() : track.GetOutBranch();
                if (behind?.track != null && !Track(behind.track, !behind.first)) return false;

                float distance = 0f;
                for (int count = 0; count < MaxTracks && distance < ScanRangeMeters; count++)
                {
                    if (!Track(track, forward)) return false;
                    distance += (float)track.GetKinkedPointSet().span;

                    var junction = forward ? track.outJunction : track.inJunction;
                    if (junction != null && junction.inBranch?.track == track && !TrackSigns.IsBuilt(junction, junction.selectedBranch))
                    {
                        if (!MayBuild()) return false;
                        TrackSigns.JunctionSpeed(junction, junction.selectedBranch);
                    }

                    var aligned = forward ? track.GetOutBranch() : track.GetInBranch();
                    if (aligned?.track == null) break;
                    track = aligned.track;
                    forward = aligned.first;
                }
                return true;
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Building the route signs ahead failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Walks the route ahead as the switches are set and collects every speed and grade sign by distance from the car.
        /// The planned route plays no part, so the table shows where the train really goes.
        /// </summary>
        private void Scan(RouteData data, List<(float, float)> speeds, List<(float, float)> grades, RailTrack track,
            bool forward, float startSpan, bool switchDamage, bool trackDamage)
        {
            string currentYard = YardOf(track);
            TrackEvent station = null;
            if (currentYard != null)
            {
                station = AddStation(data, currentYard, 0);
                NameTrack(station, track);
            }

            float firstSpan = (float)track.GetKinkedPointSet().span;
            float carFromEntry = forward ? startSpan : firstSpan - startSpan;
            AddSignsBehind(track, forward, carFromEntry, speeds, grades);

            var signals = DvSignals.ByTrack();
            var wiredByTrack = CatenaryLayout.Wired();
            var neutralByTrack = CatenaryLayout.Neutral();
            var wired = new List<(float From, float To)>();
            var neutral = new List<(float From, float To)>();
            var fedByTrack = CatenaryLayout.Fed();
            var fed = new List<(float From, float To, int Volts)>();
            var damaged = new List<SpeedRestriction>();

            // Distance from the car to the entry end of the current track. Negative for the car's own track.
            float trackStart = -carFromEntry;
            float distance = 0f;
            for (int count = 0; count < MaxTracks && distance < ScanRangeMeters; count++)
            {
                float span = (float)track.GetKinkedPointSet().span;
                foreach (var sign in TrackSigns.For(track, forward))
                {
                    float at = trackStart + sign.Fraction * span;
                    if (at < 0) continue;
                    speeds.Add((at, sign.SpeedKmh));
                    if (sign.GradePercent.HasValue)
                        grades.Add((at, sign.GradePercent.Value));
                }
                if (signals.TryGetValue(track, out var trackSignals))
                    AddSignals(data, trackSignals, forward, trackStart, span);
                AddStretches(wired, wiredByTrack, track, forward, trackStart, span);
                AddStretches(neutral, neutralByTrack, track, forward, trackStart, span);
                if (fedByTrack.TryGetValue(track, out var fedStretches))
                    foreach (var s in fedStretches)
                        fed.Add(forward ? (trackStart + s.From, trackStart + s.To, s.Volts)
                            : (trackStart + span - s.To, trackStart + span - s.From, s.Volts));
                foreach (var section in (trackDamage ? TrackDamage.On(track) : new List<DamagedSection>()).Where(s => s.LimitKmh.HasValue))
                    damaged.Add(new SpeedRestriction
                    {
                        From = trackStart + (forward ? section.From : span - section.To),
                        To = trackStart + (forward ? section.To : span - section.From),
                        LimitKmh = section.LimitKmh.Value,
                        DamagePercent = section.DamagePercent,
                    });
                distance = trackStart + span;

                if (!NextTrack(data, speeds, track, forward, distance, switchDamage, out var nextTrack, out bool nextForward))
                    break;

                string yard = YardOf(nextTrack);
                if (yard != null && yard != currentYard)
                    station = AddStation(data, yard, distance);
                if (station != null && yard == station.StationId)
                    NameTrack(station, nextTrack);
                if (yard != null)
                    AddTrack(data, nextTrack, yard, distance);
                // Switches and ladders inside a yard have no yard prefix. Keep the yard so it is not listed twice.
                if (yard != null)
                    currentYard = yard;

                track = nextTrack;
                forward = nextForward;
                trackStart = distance;
            }
            data.ScannedMeters = Mathf.Min(distance, ScanRangeMeters);
            AddCatenary(data, wired, CatenaryLayout.MaxGap, TrackEventType.WireBegin, "Overhead wire begins",
                TrackEventType.WireEnd, "End of overhead wire");
            AddCatenary(data, neutral, NeutralJoin, TrackEventType.NeutralBegin, "Neutral section",
                TrackEventType.NeutralEnd, "End of neutral section");
            AddVoltage(data, fed);
            data.Restrictions = SpeedRestrictions.Merge(damaged, RestrictionJoin);
        }

        /// <summary>Damaged sections closer than this, in m, are one restriction.</summary>
        private const float RestrictionJoin = 50f;

        /// <summary>Neutral stretches closer than this, in m, are one. Two of the steps <see cref="CatenaryLayout"/> checks a track in.</summary>
        private const float NeutralJoin = 2f * CatenaryLayout.SampleStep;

        /// <summary>Turns the stretches of one track into distances from the car along the train's direction.</summary>
        private static void AddStretches(List<(float From, float To)> into, Dictionary<RailTrack, List<(float From, float To)>> byTrack,
            RailTrack track, bool forward, float trackStart, float span)
        {
            if (!byTrack.TryGetValue(track, out var stretches)) return;
            foreach (var s in stretches)
                into.Add(forward ? (trackStart + s.From, trackStart + s.To) : (trackStart + span - s.To, trackStart + span - s.From));
        }

        /// <summary>Stretches are joined over gaps up to <paramref name="join"/> m.</summary>
        private static void AddCatenary(RouteData data, List<(float From, float To)> stretches, float join,
            TrackEventType beginType, string beginName, TrackEventType endType, string endName)
        {
            foreach (var (at, begins) in TrackStretches.Points(stretches, data.ScannedMeters, join))
                data.UpcomingEvents.Add(new TrackEvent
                {
                    EventType = begins ? beginType : endType,
                    Name = begins ? beginName : endName,
                    DistanceMeters = at,
                });
        }

        /// <summary>Sets the voltage at wire starts and neutral ends from the first fed wire within <see cref="CatenaryLayout.MaxGap"/> m.</summary>
        private static void AddVoltage(RouteData data, List<(float From, float To, int Volts)> fed)
        {
            if (fed.Count == 0) return;
            foreach (var ev in data.UpcomingEvents.Where(e => e.EventType == TrackEventType.WireBegin || e.EventType == TrackEventType.NeutralEnd))
            {
                int volts = TrackStretches.ValueAfter(fed, ev.DistanceMeters, CatenaryLayout.MaxGap);
                if (volts == 0) continue;
                ev.Value = volts;
                ev.Name += $", {volts} V";
            }
        }

        /// <summary>Adds the last speed and grade signs before the car as the values at distance 0.</summary>
        private static void AddSignsBehind(RailTrack track, bool forward, float carFromEntry,
            List<(float, float)> speeds, List<(float, float)> grades)
        {
            float span = (float)track.GetKinkedPointSet().span;
            var behind = TrackSigns.For(track, forward).Where(s => s.Fraction * span <= carFromEntry).ToList();

            var previousBranch = forward ? track.GetInBranch() : track.GetOutBranch();
            if (previousBranch?.track != null)
            {
                // Joined at the previous track's start, so the train left it backwards.
                var previous = TrackSigns.For(previousBranch.track, !previousBranch.first);
                behind.InsertRange(0, previous.Select(s => new TrackSigns.Sign(-1f, s.SpeedKmh, s.GradePercent)));
            }

            var speedSign = behind.LastOrDefault();
            if (speedSign.SpeedKmh > 0)
                speeds.Add((0f, speedSign.SpeedKmh));
            var gradeSign = behind.LastOrDefault(s => s.GradePercent.HasValue);
            if (gradeSign.GradePercent.HasValue)
                grades.Add((0f, gradeSign.GradePercent.Value));
        }

        /// <summary>Picks the track the switches lead onto and records the switch there. False where the scan must stop.</summary>
        private static bool NextTrack(RouteData data, List<(float, float)> speeds, RailTrack track, bool forward, float distance,
            bool switchDamage, out RailTrack nextTrack, out bool nextForward)
        {
            nextTrack = null;
            nextForward = true;
            var aligned = forward ? track.GetOutBranch() : track.GetInBranch();
            var junction = forward ? track.outJunction : track.inJunction;
            bool facing = junction != null && junction.inBranch?.track == track;

            if (facing)
            {
                AddSwitch(data, junction, distance, false, switchDamage);
                AddJunctionSpeed(speeds, junction, junction.selectedBranch, distance);
            }
            else if (junction != null)
            {
                AddSwitch(data, junction, distance, true, switchDamage);
            }

            if (aligned?.track == null)
            {
                data.UpcomingEvents.Add(new TrackEvent { EventType = TrackEventType.EndOfTrack, DistanceMeters = distance, Name = "End of track" });
                return false;
            }

            nextTrack = aligned.track;
            nextForward = aligned.first;
            return true;
        }

        /// <summary>DV Signals mod signals facing the train.</summary>
        private static void AddSignals(RouteData data, List<DvSignals.Placed> signals, bool forward, float trackStart, float span)
        {
            foreach (var signal in signals.Where(s => s.GuardsForward == forward))
            {
                float at = trackStart + (forward ? signal.Span : span - signal.Span);
                if (at < 0) continue;
                var aspect = DvSignals.Aspect(signal);
                data.UpcomingEvents.Add(new TrackEvent
                {
                    EventType = TrackEventType.Signal,
                    Name = $"{signal.Kind} {signal.Name}".TrimEnd(),
                    DistanceMeters = at,
                    Aspect = aspect?.id,
                    Restrictive = aspect?.restrictive ?? false,
                });
            }
        }

        /// <summary>The game signs a speed per branch just before a facing switch.</summary>
        private static void AddJunctionSpeed(List<(float, float)> speeds, Junction junction, int branch, float distance)
        {
            int? speed = TrackSigns.JunctionSpeed(junction, branch);
            if (speed.HasValue)
                speeds.Add((distance, speed.Value));
        }

        /// <summary>A <paramref name="trailing"/> switch has nothing to set, so it is listed only for its damage.</summary>
        private static void AddSwitch(RouteData data, Junction junction, float distance, bool trailing, bool withDamage)
        {
            var state = withDamage ? JunctionDamage.Read(junction) : null;
            if (trailing && (state == null || state.DamagePercent == 0 && !state.Locked)) return;
            data.UpcomingEvents.Add(new TrackEvent
            {
                EventType = TrackEventType.Junction,
                Name = junction.Name(),
                DistanceMeters = distance,
                Value = junction.selectedBranch,
                Trailing = trailing,
                Switch = state,
            });
        }

        private static TrackEvent AddStation(RouteData data, string yard, float distance)
        {
            var station = new TrackEvent
            {
                EventType = TrackEventType.Station,
                Name = Stations.Name(yard),
                StationId = yard,
                DistanceMeters = distance,
            };
            data.UpcomingEvents.Add(station);
            return station;
        }

        private static void AddTrack(RouteData data, RailTrack track, string yard, float distance)
        {
            string id = track.LogicTrack()?.ID.FullDisplayID;
            if (string.IsNullOrEmpty(id)) return;
            data.UpcomingEvents.Add(new TrackEvent
            {
                EventType = TrackEventType.Track,
                Name = id,
                StationId = yard,
                DistanceMeters = distance,
            });
        }

        /// <summary>The last track recorded wins, so a route ending in a yard names the track it ends on.</summary>
        private static void NameTrack(TrackEvent station, RailTrack track)
        {
            string id = track.LogicTrack()?.ID.FullDisplayID;
            if (!string.IsNullOrEmpty(id))
                station.Track = id;
        }

        private void Remember(TrainCar car, bool movesForward, RouteData data)
        {
            var position = car.transform.position - DV.OriginShift.OriginShift.currentMove;
            float moved = _lastPosition.HasValue ? Vector3.Distance(position, _lastPosition.Value) : 0f;
            if (car != _lastCar || movesForward != _lastMovesForward || !_lastPosition.HasValue || moved > MaxMoveMeters)
            {
                _history.Reset();
                _events.Reset();
                _odometer = 0f;
            }
            else
            {
                _odometer += moved;
            }
            _lastCar = car;
            _lastMovesForward = movesForward;
            _lastPosition = position;

            _history.Record(_odometer, data.SpeedSections);
            data.SpeedSectionsBehind = _history.Behind(_odometer);
            _events.Record(_odometer, data.UpcomingEvents);
            data.PassedEvents = _events.Behind(_odometer, PassedEventCount);
        }

        private static void AddSectionEvents(RouteData data)
        {
            // A restriction's own rows carry the limits at its ends. Where it ends on unsigned track there is none to show.
            foreach (var section in data.SpeedSections.Skip(1).Where(s => s.Value > 0f
                && !data.Restrictions.Any(r => Mathf.Approximately(r.From, s.Start) || Mathf.Approximately(r.To, s.Start))))
                data.UpcomingEvents.Add(new TrackEvent { EventType = TrackEventType.SpeedLimitChange, DistanceMeters = section.Start,
                    Value = section.Value, Name = $"{section.Value:F0} km/h" });
            foreach (var restriction in data.Restrictions)
            {
                if (restriction.From > 0f)
                    data.UpcomingEvents.Add(new TrackEvent
                    {
                        EventType = TrackEventType.TrackDamage,
                        DistanceMeters = restriction.From,
                        Value = LimitFrom(data, restriction.From),
                        Name = restriction.LimitKmh == 0
                            ? $"Track closed, damaged {restriction.DamagePercent} %"
                            : $"Track damaged {restriction.DamagePercent} %",
                    });
                if (restriction.To < data.ScannedMeters)
                    data.UpcomingEvents.Add(new TrackEvent
                    {
                        EventType = TrackEventType.TrackDamageEnd,
                        DistanceMeters = restriction.To,
                        Value = LimitFrom(data, restriction.To),
                        Name = "End of track damage",
                    });
            }
            foreach (var section in data.GradeSections.Skip(1))
                data.UpcomingEvents.Add(new TrackEvent { EventType = TrackEventType.GradeChange, DistanceMeters = section.Start,
                    Value = section.Value, Name = $"{section.Value:+0.0;-0.0;0.0} %" });
        }

        /// <summary>0 when no speed section starts at <paramref name="distance"/>.</summary>
        private static float LimitFrom(RouteData data, float distance) =>
            data.SpeedSections.LastOrDefault(s => s.Start <= distance)?.Value ?? 0f;

        private static DV.PointSet.EquiPointSet.Point ClosestPoint(DV.PointSet.EquiPointSet points, Vector3 position)
        {
            var best = points.points[0];
            float bestDistance = float.MaxValue;
            foreach (var point in points.points)
            {
                float d = (TrackGeometry.ToVector3(point.position) - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = point;
                }
            }
            return best;
        }

        /// <summary>Station yard of a track, from its logic ID prefix.</summary>
        private static string YardOf(RailTrack track)
        {
            string id = track.LogicTrack()?.ID.FullID;
            if (string.IsNullOrEmpty(id)) return null;
            string prefix = id.Split('-')[0];
            return StationController.allStations.Any(s => s?.stationInfo?.YardID == prefix) ? prefix : null;
        }

        public void Dispose() { }
    }
}
