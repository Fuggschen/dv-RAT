using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Utils;

namespace DVRAT
{
    /// <summary>Track stretches under the 1500 V DC Catenary mod's wires, read by reflection. The mod links neutral wires to the substation "NEUTRAL". A point is neutral only when no fed wire also covers it.</summary>
    public static class CatenaryLayout
    {
        private const string NeutralSubstation = "NEUTRAL";

        /// <summary>A gap this long (m) with wire at both ends is crossed on momentum and does not end the wire.</summary>
        public const float MaxGap = 60f;

        /// <summary>Distance between the points checked along a track, in m.</summary>
        public const float SampleStep = 2f;

        /// <summary>An overhead wire this close (m) to the track's centre line touches the pantograph. Covers the zigzag.</summary>
        private const double Reach = 1.0;

        /// <summary>Side rails run 2.3 m beside the track. Matched from SideInner to <see cref="SideOuter"/> m from the centre, short of the next track's wire.</summary>
        private const double SideInner = 1.6, SideOuter = 3.0;

        private static readonly HashSet<string> SideRailTypes = new HashSet<string> { "side_rail", "termination_rail" };

        /// <summary>Edge in m of the squares wires are sorted into.</summary>
        private const double CellSize = 50.0;

        /// <summary>The mod keeps positions in mm.</summary>
        private const double MetresPerFixed = 0.001;

        private const float RetrySeconds = 30f;

        private struct Segment
        {
            public double AX, AZ, BX, BZ;
            public bool Neutral, Side;

            /// <summary>System voltage of the wire's substation. 0 for a neutral wire or when unknown.</summary>
            public int Volts;
        }

        /// <summary>DC systems a substation's supply voltage is rounded to. Supply runs above nominal.</summary>
        private static readonly int[] SystemVolts = { 600, 750, 1000, 1500, 3000 };

        private static readonly Dictionary<RailTrack, List<(float From, float To)>> None =
            new Dictionary<RailTrack, List<(float From, float To)>>();

        private static bool _searched, _failed;
        private static Type _wireType;
        private static FieldInfo _objects, _substation, _line, _originX, _originZ, _directionX, _directionZ;
        private static PropertyInfo _wireKind;
        private static Type _substationType;
        private static FieldInfo _substationName, _supplyVoltage;

        private static object _builtFor;
        private static int _builtTrackCount;
        private static float _nextTry;
        private static Dictionary<RailTrack, List<(float From, float To)>> _wired = None, _neutral = None;

        private static readonly Dictionary<RailTrack, List<(float From, float To, int Volts)>> NoneFed =
            new Dictionary<RailTrack, List<(float From, float To, int Volts)>>();

        private static Dictionary<RailTrack, List<(float From, float To, int Volts)>> _fed = NoneFed;

        /// <summary>Stretches under any wire, in m from the track's start. Empty without the mod's wire. Main thread only.</summary>
        public static Dictionary<RailTrack, List<(float From, float To)>> Wired() => Build() ? _wired : None;

        /// <summary>Neutral stretches, in m from the track's start. Empty without the mod's wire. Main thread only.</summary>
        public static Dictionary<RailTrack, List<(float From, float To)>> Neutral() => Build() ? _neutral : None;

        /// <summary>Stretches under a fed wire with its system voltage. A stretch ends where the voltage changes. Main thread only.</summary>
        public static Dictionary<RailTrack, List<(float From, float To, int Volts)>> Fed() => Build() ? _fed : NoneFed;

        /// <summary>Builds the stretches behind the loading screen, not in the frame the tablet boots in.</summary>
        internal static void OnWorldLoaded()
        {
            // A new world has new tracks even when the wire system and track count match.
            _builtFor = null;
            _nextTry = 0f;
            // Must not throw into the game's loading routine.
            try
            {
                Build();
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"Could not find the catenary's wires while loading: {ex.Message}");
            }
            // No wires yet: retry on first use, not after the delay.
            if (_builtFor == null)
                _nextTry = 0f;
        }

        /// <summary>Rebuilds when the tracks or the wire system change. False while there is nothing to show.</summary>
        private static bool Build()
        {
            object system = CatenaryWires.System();
            if (system == null || !Find(system.GetType())) return false;
            var tracks = SingletonBehaviour<RailTrackRegistryBase>.Instance.AllTracks;
            if (_builtFor == system && _builtTrackCount == tracks.Length) return true;
            // The mod puts its wires up after the world loads, so retry until some are found.
            if (UnityEngine.Time.realtimeSinceStartup < _nextTry) return false;
            _nextTry = UnityEngine.Time.realtimeSinceStartup + RetrySeconds;

            try
            {
                var wired = new Dictionary<RailTrack, List<(float From, float To)>>();
                var neutral = new Dictionary<RailTrack, List<(float From, float To)>>();
                var fed = new Dictionary<RailTrack, List<(float From, float To, int Volts)>>();
                using (Timing.Measure("Finding the tracks under the catenary's wires"))
                    Sample(Cells(system), tracks, wired, neutral, fed);
                RATMain.Log($"{wired.Count} tracks are under the catenary's wires, {neutral.Count} have a neutral section");
                if (wired.Count == 0) return false;
                _wired = wired;
                _neutral = neutral;
                _fed = fed;
                _builtFor = system;
                _builtTrackCount = tracks.Length;
                return true;
            }
            catch (Exception ex)
            {
                Fail($"reading the wires failed: {(ex.InnerException ?? ex).Message}");
                return false;
            }
        }

        private static Dictionary<(int, int), List<Segment>> Cells(object system)
        {
            var cells = new Dictionary<(int, int), List<Segment>>();
            var volts = SubstationVolts(system);
            foreach (var item in (IEnumerable)_objects.GetValue(system))
            {
                if (item == null || !_wireType.IsInstanceOfType(item)) continue;
                var segment = SegmentOf(item, volts);
                for (int x = Cell(Math.Min(segment.AX, segment.BX) - SideOuter); x <= Cell(Math.Max(segment.AX, segment.BX) + SideOuter); x++)
                    for (int z = Cell(Math.Min(segment.AZ, segment.BZ) - SideOuter); z <= Cell(Math.Max(segment.AZ, segment.BZ) + SideOuter); z++)
                    {
                        if (!cells.TryGetValue((x, z), out var list))
                            cells[(x, z)] = list = new List<Segment>();
                        list.Add(segment);
                    }
            }
            return cells;
        }

        private static int Cell(double metres) => (int)Math.Floor(metres / CellSize);

        /// <summary>System voltage per substation name, rounded from the supply voltage. Empty when unreadable.</summary>
        private static Dictionary<string, int> SubstationVolts(object system)
        {
            var volts = new Dictionary<string, int>();
            if (_substationName == null || _supplyVoltage == null) return volts;
            foreach (var item in (IEnumerable)_objects.GetValue(system))
            {
                if (item == null || !_substationType.IsInstanceOfType(item)) continue;
                if (!(_substationName.GetValue(item) is string name)) continue;
                float supply = (float)_supplyVoltage.GetValue(item);
                volts[name] = SystemVolts.OrderBy(v => Math.Abs(v - supply)).First();
            }
            return volts;
        }

        private static void Sample(Dictionary<(int, int), List<Segment>> cells, RailTrack[] tracks,
            Dictionary<RailTrack, List<(float From, float To)>> wired, Dictionary<RailTrack, List<(float From, float To)>> neutral,
            Dictionary<RailTrack, List<(float From, float To, int Volts)>> fed)
        {
            if (cells.Count == 0) return;
            foreach (var track in tracks)
            {
                var points = track?.GetKinkedPointSet();
                if (points?.points == null || points.points.Length == 0) continue;

                float span = (float)points.span;
                int count = Math.Max(1, (int)Math.Ceiling(span / SampleStep));
                bool[] under = null, unfed = null;
                int[] fedVolts = null;
                for (int i = 0; i < count; i++)
                {
                    // Clamped to the double span, as a float can round past the end and the point set refuses it.
                    var p = points.points[points.GetPointIndexForSpan(Math.Min((i + 0.5) * SampleStep, points.span))].position;
                    if (!cells.TryGetValue((Cell(p.x), Cell(p.z)), out var near)) continue;
                    bool touchesNeutral = false, touchesFed = false;
                    int volts = 0;
                    foreach (var segment in near)
                    {
                        if (!Touches(segment, p.x, p.z)) continue;
                        if (segment.Neutral) touchesNeutral = true;
                        else
                        {
                            touchesFed = true;
                            volts = Math.Max(volts, segment.Volts);
                        }
                    }
                    if (!touchesNeutral && !touchesFed) continue;
                    (under ?? (under = new bool[count]))[i] = true;
                    if (touchesNeutral && !touchesFed)
                        (unfed ?? (unfed = new bool[count]))[i] = true;
                    if (volts > 0)
                        (fedVolts ?? (fedVolts = new int[count]))[i] = volts;
                }
                if (under != null) wired[track] = TrackStretches.FromSamples(under, SampleStep, span);
                if (unfed != null) neutral[track] = TrackStretches.FromSamples(unfed, SampleStep, span);
                if (fedVolts != null) fed[track] = TrackStretches.Runs(fedVolts, SampleStep, span);
            }
        }

        private static Segment SegmentOf(object wire, Dictionary<string, int> substationVolts)
        {
            string substation = _substation.GetValue(wire) as string;
            object line = _line.GetValue(wire);
            int x = (int)_originX.GetValue(line), z = (int)_originZ.GetValue(line);
            int dx = (int)_directionX.GetValue(line), dz = (int)_directionZ.GetValue(line);
            return new Segment
            {
                AX = x * MetresPerFixed, AZ = z * MetresPerFixed,
                BX = (x + dx) * MetresPerFixed, BZ = (z + dz) * MetresPerFixed,
                Neutral = substation == NeutralSubstation,
                Volts = substation != null && substationVolts.TryGetValue(substation, out int volts) ? volts : 0,
                Side = SideRailTypes.Contains(_wireKind.GetValue(wire) as string ?? ""),
            };
        }

        private static bool Touches(Segment s, double x, double z)
        {
            double dx = s.BX - s.AX, dz = s.BZ - s.AZ;
            double lengthSquared = dx * dx + dz * dz;
            double t = lengthSquared > 0 ? ((x - s.AX) * dx + (z - s.AZ) * dz) / lengthSquared : 0;
            t = Math.Max(0, Math.Min(1, t));
            double ox = s.AX + t * dx - x, oz = s.AZ + t * dz - z;
            double distanceSquared = ox * ox + oz * oz;
            return s.Side
                ? distanceSquared >= SideInner * SideInner && distanceSquared <= SideOuter * SideOuter
                : distanceSquared <= Reach * Reach;
        }

        private static bool Find(Type systemType)
        {
            if (_failed) return false;
            if (_searched) return true;
            _searched = true;

            const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            _objects = systemType.GetField("_all_objects", Instance);
            _wireType = systemType.GetNestedType("wire", BindingFlags.Public | BindingFlags.NonPublic);
            _substation = _wireType?.GetField("substation", Instance);
            _wireKind = _wireType?.GetProperty("wire_type", Instance);
            // Optional. Without them the wire shows no voltage.
            _substationType = systemType.GetNestedType("substation", BindingFlags.Public | BindingFlags.NonPublic);
            _substationName = _substationType?.GetField("map_location", Instance);
            _supplyVoltage = _substationType?.GetField("supply_voltage", Instance);
            if (_supplyVoltage?.FieldType != typeof(float)) _supplyVoltage = null;
            _line = _wireType?.GetField("_pantograph_strip_intersection", Instance);
            var lineType = _line?.FieldType;
            _originX = lineType?.GetField("_crossed_origin_x", Instance);
            _originZ = lineType?.GetField("_crossed_origin_z", Instance);
            _directionX = lineType?.GetField("_crossed_direction_x", Instance);
            _directionZ = lineType?.GetField("_crossed_direction_z", Instance);
            if (_objects == null || _substation == null || _wireKind == null || _originX == null || _originZ == null
                || _directionX == null || _directionZ == null)
            {
                Fail("its wires were not found");
                return false;
            }
            return true;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"1500 V DC Catenary: {reason}. The Route page shows no wire or neutral sections.");
        }
    }
}
