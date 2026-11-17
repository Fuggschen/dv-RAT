using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.PointSet;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Finds which tracks lie under the 1500 V DC Catenary mod's wire, by calling its internal absolute_wire_height_and_voltage through reflection. A missing member switches this off with one log warning.</summary>
    public static class CatenaryWires
    {
        private static readonly ModWatch Mod = new ModWatch("Catenary-DC");
        private const string SystemType = "electric_sim.catenary.overhead_equipment";
        private const string QueryMethod = "absolute_wire_height_and_voltage";

        /// <summary>Distance between the points asked along a track, in m.</summary>
        private const float SampleSpacing = 20f;

        /// <summary>Half the length of the strip across the rails. Covers the wire's zigzag.</summary>
        private const float StripHalfWidth = 0.8f;

        /// <summary>Side rails (2.3 m beside the track, as in SM yards) are searched from here to <see cref="SideRailOuter"/>, in m from the centre.</summary>
        private const float SideRailInner = 1.6f;

        /// <summary>Stops short of the next track's overhead wire.</summary>
        private const float SideRailOuter = 3f;

        /// <summary>Wire is searched above this height over the rails, so nothing below a pantograph counts.</summary>
        private const float BaseAboveRail = 2f;

        /// <summary>Share of a track's points that must be under wire. Short gaps at section breaks do not count.</summary>
        private const float WiredShare = 0.75f;

        /// <summary>Shorter tracks (switches, crossovers) are sampled every <see cref="ShortSpacing"/> m and count as wired if any point is, so one gap does not cut routes through a junction.</summary>
        private const float ShortTrack = 2f * SampleSpacing;
        private const float ShortSpacing = 5f;

        /// <summary>The mod keeps positions in mm.</summary>
        private const double FixedPerMetre = 1000.0;

        private static bool _searched, _failed;
        private static PropertyInfo _system;
        private static MethodInfo _query;
        private static FieldInfo _height;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        public static bool Present => System() != null;

        /// <summary>Indices of tracks under wire. Null without wire or while none is found. Unfed wire counts, so a tripped substation does not drop its line.</summary>
        public static HashSet<int> Survey(TrackNetwork network)
        {
            object system = System();
            if (system == null) return null;
            try
            {
                var wired = new HashSet<int>();
                using (Timing.Measure("Finding the tracks under the overhead wire"))
                    for (int i = 0; i < network.Tracks.Count; i++)
                        if (IsWired(system, network.Tracks[i]?.GetKinkedPointSet()))
                            wired.Add(i);
                RATMain.Log($"{wired.Count} of {network.Tracks.Count} tracks are under the overhead wire");
                // The mod fills its wires after the world loads, so ask again later.
                return wired.Count > 0 ? wired : null;
            }
            catch (Exception ex)
            {
                Fail($"asking for the wire failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        /// <summary>Points under wire out of the points sampled on the track. Null without the wire.</summary>
        public static (int Under, int Samples)? Coverage(RailTrack track)
        {
            object system = System();
            if (system == null || track == null) return null;
            try
            {
                return Count(system, track.GetKinkedPointSet());
            }
            catch (Exception ex)
            {
                Fail($"asking for the wire failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        private static bool IsWired(object system, EquiPointSet points)
        {
            var (under, samples) = Count(system, points);
            if (samples == 0) return false;
            return points.span < ShortTrack ? under > 0 : under >= samples * WiredShare;
        }

        private static (int Under, int Samples) Count(object system, EquiPointSet points)
        {
            if (points?.points == null || points.points.Length == 0) return (0, 0);
            int samples = points.span < ShortTrack
                ? Math.Max(3, (int)(points.span / ShortSpacing))
                : (int)(points.span / SampleSpacing);
            int under = 0;
            for (int s = 0; s < samples; s++)
            {
                var point = points.points[points.GetPointIndexForSpan((s + 0.5) * points.span / samples)];
                if (WireAbove(system, point)) under++;
            }
            return (under, samples);
        }

        /// <summary>The mod's x and z lack the floating origin, but it compares heights in the scene, so the origin shift is added to y.</summary>
        private static bool WireAbove(object system, EquiPointSet.Point point)
        {
            var across = new Vector3(-point.forward.z, 0f, point.forward.x).normalized;
            float baseY = (float)point.position.y + DV.OriginShift.OriginShift.currentMove.y + BaseAboveRail;
            return Touches(system, point, across, -StripHalfWidth, StripHalfWidth, baseY)
                || Touches(system, point, across, SideRailInner, SideRailOuter, baseY)
                || Touches(system, point, across, -SideRailOuter, -SideRailInner, baseY);
        }

        private static bool Touches(object system, EquiPointSet.Point point, Vector3 across, float from, float to, float baseY)
        {
            int Fixed(double metres) => (int)Math.Round(metres * FixedPerMetre);
            object result = _query.Invoke(system, new object[]
            {
                Fixed(point.position.x + across.x * from), Fixed(point.position.z + across.z * from),
                Fixed(point.position.x + across.x * to), Fixed(point.position.z + across.z * to),
                baseY,
                // No load, as substations add up the draw passed through this call.
                0f,
            });
            return _height.GetValue(result) != null;
        }

        /// <summary>Null while the mod is off or has no wire.</summary>
        internal static object System()
        {
            if (!Find()) return null;
            try
            {
                return _system.GetValue(null);
            }
            catch (TargetInvocationException)
            {
                // The getter throws without a catenary, e.g. with the Double-Track layout.
                return null;
            }
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var type = Mod.FindType(SystemType);
                if (type == null)
                {
                    Fail($"{SystemType} was not found");
                    return false;
                }
                _system = type.GetProperty("system", BindingFlags.Public | BindingFlags.Static);
                _query = type.GetMethod(QueryMethod, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) }, null);
                _height = _query?.ReturnType.GetField("Item1");
                if (_system == null || _query == null || _height == null)
                {
                    Fail($"{SystemType}.{QueryMethod} was not found");
                    return false;
                }
                RATMain.Log("1500 V DC Catenary found, the route planner can keep to tracks under its wire");
            }
            return _query != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"1500 V DC Catenary: {reason}. The route planner cannot keep to the wire.");
        }
    }
}
