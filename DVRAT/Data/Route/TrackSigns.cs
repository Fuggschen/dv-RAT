using System;
using System.Collections.Generic;
using System.Linq;
using DV.Signs;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Speed and grade signs along a track, ported from <c>DV.Signs.SignPlacer</c>, which keeps its results private.</summary>
    public static class TrackSigns
    {
        public readonly struct Sign
        {
            public Sign(float fraction, int speedKmh, float? gradePercent)
            {
                Fraction = fraction;
                SpeedKmh = speedKmh;
                GradePercent = gradePercent;
            }

            /// <summary>0 to 1 of the track length, from the end the train enters at.</summary>
            public float Fraction { get; }
            public int SpeedKmh { get; }

            /// <summary>Positive uphill. Null when the sign shows no grade.</summary>
            public float? GradePercent { get; }
        }

        // SignPlacer's constants and default inspector values. The scene instance overrides the latter.
        private const float FirstTrackSignDistance = 15f;
        private const float SignPullBackDistance = 100f;
        private const float MinTrackLength = 100f;
        private const float FlatGradeThreshold = 0.005f;
        private const float JunctionDivergingMaxSpeed = 60f;
        private const float YardOrShortTrackMaxSpeed = 50f;

        private static float _error = 1f;
        private static float _minArcLength = 300f;
        private static float _speedDiffThreshold = 30f;
        private static float _speedSegmentLengthThreshold = 300f;
        private static float _lastSegmentOptimalLength = 250f;
        private static List<string> _noSignMarks = new List<string>();
        private static bool _settingsRead;

        // Never cleared: building takes most of a second, and another save's tracks are other objects.
        private static readonly Dictionary<(RailTrack, bool), List<Sign>> TrackCache = new Dictionary<(RailTrack, bool), List<Sign>>();
        private static readonly Dictionary<(Junction, int), int?> JunctionCache = new Dictionary<(Junction, int), int?>();

        // Arcs are most of the work, and one curve is read for both directions and for neighbouring junction signs.
        private static readonly Dictionary<BezierCurve, List<BezierArcApproximation.Arc>> ArcCache =
            new Dictionary<BezierCurve, List<BezierArcApproximation.Arc>>();

        private class Segment
        {
            public BezierCurve Curve;
            public float Length;
            public float MinRadius;
            public float StartT;
            public float EndT;
            public float AssignedSpeed;

            public float Speed => AssignedSpeed > 0f ? AssignedSpeed : SpeedForRadius(MinRadius);

            public float GradeAngle
            {
                get
                {
                    Vector3 a = Curve.GetPointAt(StartT), b = Curve.GetPointAt(EndT);
                    float distance = Vector3.Distance(a, b);
                    return distance < 0.001f ? 0f : Mathf.Abs(a.y - b.y) / distance;
                }
            }

            public float SignedGradePercent
            {
                get
                {
                    float angle = GradeAngle;
                    if (angle < FlatGradeThreshold) return 0f;
                    bool incline = Curve.GetPointAt(EndT).y > Curve.GetPointAt(StartT).y;
                    return (float)Math.Round(100f * angle, 1) * (incline ? 1f : -1f);
                }
            }

            public Segment Copy() => (Segment)MemberwiseClone();
        }

        /// <summary>The game's table from minimum curve radius to limit.</summary>
        public static float SpeedForRadius(float radius)
        {
            if (radius < 50f) return 10f;
            if (radius < 70f) return 20f;
            if (radius < 95f) return 30f;
            if (radius < 130f) return 40f;
            if (radius < 170f) return 50f;
            if (radius < 230f) return 60f;
            if (radius < 360f) return 70f;
            if (radius < 700f) return 80f;
            if (radius < 900f) return 90f;
            if (radius < 1200f) return 100f;
            return 120f;
        }

        public static bool IsBuilt(RailTrack track, bool forward) => TrackCache.ContainsKey((track, forward));

        public static bool IsBuilt(Junction junction, int branch) => JunctionCache.ContainsKey((junction, branch));

        /// <summary>Forward means in to out end.</summary>
        public static IReadOnlyList<Sign> For(RailTrack track, bool forward)
        {
            ReadSettings();
            var key = (track, forward);
            if (!TrackCache.TryGetValue(key, out var signs))
            {
                try
                {
                    signs = BuildTrackSigns(track, reverse: !forward);
                }
                catch (Exception ex)
                {
                    RATMain.LogWarning($"Could not compute signs for {track.name}: {ex.Message}");
                    signs = new List<Sign>();
                }
                TrackCache[key] = signs;
            }
            return signs;
        }

        /// <summary>Speed of the junction sign for <paramref name="branch"/> when approaching from the in branch. Null where the game places none.</summary>
        public static int? JunctionSpeed(Junction junction, int branch)
        {
            ReadSettings();
            var key = (junction, branch);
            if (!JunctionCache.TryGetValue(key, out var speed))
            {
                try
                {
                    speed = BuildJunctionSpeed(junction, branch);
                }
                catch (Exception ex)
                {
                    RATMain.LogWarning($"Could not compute junction sign for {junction.name}: {ex.Message}");
                    speed = null;
                }
                JunctionCache[key] = speed;
            }
            return speed;
        }

        public static bool HasArcs(BezierCurve curve) => ArcCache.ContainsKey(curve);

        /// <summary>The slow part of a track's signs, runnable on its own.</summary>
        public static void BuildArcs(BezierCurve curve)
        {
            ReadSettings();
            Arcs(curve);
        }

        private static List<BezierArcApproximation.Arc> Arcs(BezierCurve curve)
        {
            if (!ArcCache.TryGetValue(curve, out var arcs))
            {
                arcs = new List<BezierArcApproximation.Arc>();
                BezierArcApproximation.CalculateArcs(curve, _error, arcs);
                ArcCache[curve] = arcs;
            }
            return arcs;
        }

        /// <summary>For <see cref="WorldStreamingInit.LoadingFinished"/>. Reads the sign settings behind the loading screen, as finding them took about 70 ms.</summary>
        internal static void OnWorldLoaded()
        {
            // An exception here would reach the game's loading routine.
            try
            {
                _settingsRead = false;
                ReadSettings();
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Could not read the sign settings while loading: {ex.Message}");
            }
        }

        private static void ReadSettings()
        {
            if (_settingsRead) return;
            _settingsRead = true;
            var placer = UnityEngine.Object.FindObjectOfType<SignPlacer>();
            if (placer == null) return;

            _error = placer.error;
            _minArcLength = placer.minArcLength;
            _speedDiffThreshold = placer.minimizeSpeedDiffThreshold;
            _speedSegmentLengthThreshold = placer.minimizeSpeedSegmentLengthThreshold;
            _lastSegmentOptimalLength = placer.lastSegmentOptimalLength;
            _noSignMarks = placer.noSignsTrackNameMarks ?? new List<string>();
        }

        private static bool HasTrackSigns(RailTrack track)
        {
            if (track.curve.length < MinTrackLength) return false;
            if (track.name.StartsWith("[Y]", StringComparison.Ordinal) || track.name.StartsWith("[#]", StringComparison.Ordinal)) return false;
            return true;
        }

        private static bool SignsSuppressed(RailTrack track) => _noSignMarks.Any(mark => track.name.StartsWith(mark, StringComparison.Ordinal));

        #region Port of SignPlacer

        private static List<Segment> Segments(BezierCurve curve, bool reverse)
        {
            // A copy: the reverse direction flips the arcs in place.
            var arcs = new List<BezierArcApproximation.Arc>(Arcs(curve));
            if (reverse)
            {
                arcs.Reverse();
                for (int i = 0; i < arcs.Count; i++)
                {
                    var arc = arcs[i];
                    var flipped = arc;
                    flipped.s = arc.e;
                    flipped.e = arc.s;
                    flipped.bezierStartT = arc.bezierEndT;
                    flipped.bezierEndT = arc.bezierStartT;
                    arcs[i] = flipped;
                }
            }

            var chunks = SignPlacerUtils.ChunkifyNumbers(arcs.Select(a => a.Length).ToList(), _minArcLength);
            var groups = new List<List<BezierArcApproximation.Arc>>();
            int index = 0;
            foreach (var chunk in chunks)
            {
                var group = new List<BezierArcApproximation.Arc>();
                for (int i = 0; i < chunk.Count; i++)
                    group.Add(arcs[index++]);
                groups.Add(group);
            }

            var segments = new List<Segment>();
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var segment = new Segment
                {
                    Curve = curve,
                    StartT = group.First().bezierStartT,
                    EndT = group.Last().bezierEndT,
                    MinRadius = float.PositiveInfinity,
                };
                foreach (var arc in group)
                {
                    segment.Length += arc.Length;
                    segment.MinRadius = Mathf.Min(segment.MinRadius, arc.r);
                }

                // Look ahead into the next segment, as far as the train travels in two seconds at this speed.
                if (g + 1 < groups.Count)
                {
                    float ahead = MinRadiusInFirstMeters(groups[g + 1], segment.Speed * 2f);
                    if (ahead < segment.MinRadius)
                        segment.MinRadius = (Mathf.Clamp(segment.MinRadius, ahead, 1200f) + ahead) / 2f;
                }
                segments.Add(segment);
            }
            return segments;
        }

        private static float MinRadiusInFirstMeters(List<BezierArcApproximation.Arc> arcs, float meters)
        {
            float min = float.PositiveInfinity;
            float covered = 0f;
            foreach (var arc in arcs)
            {
                if (covered >= meters) break;
                covered += arc.Length;
                min = Mathf.Min(min, arc.r);
            }
            return min;
        }

        private static List<Segment> SplitLastSegment(List<Segment> segments, float optimalLength)
        {
            segments = new List<Segment>(segments);
            var last = segments.Last();
            bool reversed = last.StartT > last.EndT;
            if (last.Length > 2f * optimalLength)
            {
                var tail = last.Copy();
                last.Length -= optimalLength;
                tail.Length = optimalLength;
                float t = optimalLength / last.Curve.length;
                last.EndT += reversed ? t : -t;
                tail.StartT = last.EndT;
                segments.Add(tail);
            }
            else if (last.Length > 1.5f * optimalLength)
            {
                var tail = last.Copy();
                tail.Length = last.Length *= 0.5f;
                tail.StartT = last.EndT = (last.StartT + last.EndT) * 0.5f;
                segments.Add(tail);
            }
            return segments;
        }

        private static List<Segment> MergeSameSpeed(List<Segment> segments)
        {
            segments = new List<Segment>(segments);
            for (int i = segments.Count - 1; i > 0; i--)
            {
                var before = segments[i - 1];
                var current = segments[i];
                if (Mathf.Abs(current.GradeAngle - before.GradeAngle) >= FlatGradeThreshold) continue;
                if (before.Speed != current.Speed) continue;

                before.EndT = current.EndT;
                before.Length += current.Length;
                before.MinRadius = Mathf.Min(before.MinRadius, current.MinRadius);
                segments.RemoveAt(i);
            }
            return segments;
        }

        private static List<Segment> MinimizeSpeedDifference(List<Segment> segments, float speedThreshold, float lengthThreshold)
        {
            segments = new List<Segment>(segments);
            var operations = SignPlacerUtils.MinimizeSpeedDifference(
                segments.Select(s => (s.Speed, s.Length)).ToList(), speedThreshold, lengthThreshold);
            foreach (var (op, index, value) in operations)
            {
                if (op == SignPlacerUtils.Operation.Insert)
                {
                    var before = segments[index - 1];
                    var after = segments[index];
                    var inserted = new Segment
                    {
                        Curve = before.Curve,
                        AssignedSpeed = value,
                        StartT = (before.StartT + before.EndT) / 2f,
                        EndT = after.StartT,
                        MinRadius = before.MinRadius,
                    };
                    before.EndT = inserted.StartT;
                    before.Length /= 2f;
                    inserted.Length = before.Length;
                    segments.Insert(index, inserted);
                }
                else
                {
                    segments[index].AssignedSpeed = value;
                }
            }
            return segments;
        }

        private static Segment FirstSegmentAfterJunction(Junction.Branch branch)
        {
            if (branch.track == null) return null;
            var junction = branch.first ? branch.track.outJunction : branch.track.inJunction;
            var inBranch = junction?.inBranch;
            if (inBranch?.track == null) return null;

            var first = Segments(inBranch.track.curve, reverse: !inBranch.first)[0];
            if (!HasTrackSigns(inBranch.track))
                first.AssignedSpeed = Mathf.Min(YardOrShortTrackMaxSpeed, first.Speed);
            return first;
        }

        private static List<Sign> BuildTrackSigns(RailTrack track, bool reverse)
        {
            var result = new List<Sign>();
            if (!HasTrackSigns(track) || SignsSuppressed(track)) return result;

            var curve = track.curve;
            var segments = SplitLastSegment(Segments(curve, reverse), _lastSegmentOptimalLength);
            var last = segments.Last();
            bool connected = reverse ? track.inIsConnected : track.outIsConnected;
            bool endsAtJunction = (reverse ? track.inJunction : track.outJunction) != null;
            var nextBranch = reverse ? track.inBranch : track.outBranch;
            bool intoDiverging = connected && !endsAtJunction && nextBranch.track.name == "[track diverging]";

            float endCap = float.PositiveInfinity;
            if (!connected)
                endCap = 20f;
            else if (intoDiverging || endsAtJunction)
                endCap = 60f;
            else
            {
                var after = FirstSegmentAfterJunction(nextBranch);
                if (after != null) endCap = after.Speed;
            }
            if (last.Speed > endCap)
                last.AssignedSpeed = Mathf.Clamp(last.Speed, 0f, endCap);

            segments = MergeSameSpeed(segments);
            segments = MinimizeSpeedDifference(segments, _speedDiffThreshold, _speedSegmentLengthThreshold);

            bool startConnected = reverse ? track.outIsConnected : track.inIsConnected;
            for (int i = 0; i < segments.Count; i++)
            {
                var current = segments[i];
                var before = segments[Mathf.Max(0, i - 1)];
                var after = segments[Mathf.Min(i + 1, segments.Count - 1)];
                if (current.Speed > after.Speed && current.Length <= 200f)
                    current.AssignedSpeed = after.Speed;

                float placementT;
                if (i == 0)
                {
                    if (!startConnected) continue;
                    float offset = FirstTrackSignDistance / curve.length;
                    placementT = Mathf.Clamp01(current.StartT + (reverse ? -offset : offset));
                }
                else
                {
                    // Speed drops are signed early, so the train has room to brake.
                    float pullBack = current.Speed < before.Speed ? SignPullBackDistance / curve.length : 0f;
                    placementT = Mathf.Clamp01(current.StartT - (reverse ? -pullBack : pullBack));
                }

                if (i == segments.Count - 1)
                    current.AssignedSpeed = Mathf.Min(before.Speed, current.Speed, 60f);

                bool showsGrade = i < 1 || Mathf.Abs(current.GradeAngle - before.GradeAngle) >= FlatGradeThreshold;
                float along = reverse ? 1f - placementT : placementT;
                result.Add(new Sign(along, Mathf.FloorToInt(current.Speed),
                    showsGrade ? current.SignedGradePercent : (float?)null));
            }
            return result;
        }

        private static int? BuildJunctionSpeed(Junction junction, int branch)
        {
            if (branch < 0 || branch > 1 || junction.inBranch?.track == null) return null;
            if (!HasTrackSigns(junction.inBranch.track) || junction.inBranch.track.curve.length <= 8f) return null;

            var outBranch = junction.outBranches[branch];
            var after = outBranch.first ? outBranch.track.outBranch : outBranch.track.inBranch;
            if (after?.track == null) return null;

            var segments = MinimizeSpeedDifference(Segments(after.track.curve, reverse: !after.first), _speedDiffThreshold, float.MaxValue);
            var first = segments[0];
            if (!HasTrackSigns(after.track))
                first.AssignedSpeed = Mathf.Min(YardOrShortTrackMaxSpeed, first.Speed);
            bool leftHand = junction.transform.parent != null && junction.transform.parent.name.Contains("left");
            if ((branch == 1) ^ leftHand)
                first.AssignedSpeed = Mathf.Min(JunctionDivergingMaxSpeed, first.Speed);
            return Mathf.FloorToInt(first.Speed);
        }

        #endregion
    }
}
