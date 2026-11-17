using System.Collections.Generic;
using System.Reflection;
using DV;
using DV.Damage;
using DV.Openables;
using DV.Utils;
using HarmonyLib;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Which closed cab a point is in, by the game's own muffling rules, applied to any point. Main thread only.
    /// Positions are compared in the car's frame: the "[interior]" object lags the car by a frame (0.4 m at 100 km/h).
    /// </summary>
    public static class CabZones
    {
        private static readonly FieldInfo SwitchersField =
            AccessTools.Field(typeof(InternalExternalSnapshotSwitcherManager), "switchers");
        private static readonly FieldInfo WindowsField =
            AccessTools.Field(typeof(InternalExternalSnapshotSwitcherDoorsAndWindows), "windowsBreakingController");
        private static readonly FieldInfo DoorsField =
            AccessTools.Field(typeof(InternalExternalSnapshotSwitcherDoorsAndWindows), "doorsAndWindowsController");

        // Computed once per frame.
        private static readonly List<Cab> Closed = new List<Cab>();
        private static CameraTrigger _listener;
        private static int _frame = -1;

        // Cache of each box's interior, since the parent lookup was costly per frame. Rebuilt each frame so deleted cars drop out.
        private static Dictionary<CameraTrigger, TrainCarInteriorObject> _interiors = new Dictionary<CameraTrigger, TrainCarInteriorObject>();
        private static Dictionary<CameraTrigger, TrainCarInteriorObject> _seen = new Dictionary<CameraTrigger, TrainCarInteriorObject>();

        private struct Cab
        {
            public CameraTrigger Trigger;
            // Null when the box hangs on the car itself or on nothing.
            public TrainCar InteriorOf;
        }

        /// <summary>True when a closed cab wall is between the source and the camera.</summary>
        public static bool Walled(Transform source)
        {
            Update();
            return ZoneOf(source) != _listener;
        }

        private static CameraTrigger ZoneOf(Transform at)
        {
            if (at == null) return null;
            Vector3 point = OnCarBody(at);
            foreach (var cab in Closed)
                if (cab.Trigger.IsPointInside(ForCab(cab, point)))
                    return cab.Trigger;
            return null;
        }

        // Corrects a point on an interior for the frame the interior lags.
        private static Vector3 OnCarBody(Transform at)
        {
            var interior = at.GetComponentInParent<TrainCarInteriorObject>();
            var car = interior != null ? interior.actualTrainCar : null;
            return car != null ? car.transform.TransformPoint(interior.transform.InverseTransformPoint(at.position)) : at.position;
        }

        private static Vector3 ForCab(Cab cab, Vector3 onBody)
        {
            var car = cab.InteriorOf;
            return car != null && car.interior != null ? car.interior.TransformPoint(car.transform.InverseTransformPoint(onBody)) : onBody;
        }

        private static void Update()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Closed.Clear();
            _listener = null;
            if (SwitchersField == null) return;
            var manager = SingletonBehaviour<InternalExternalSnapshotSwitcherManager>.Instance;
            if (manager == null || !(SwitchersField.GetValue(manager) is HashSet<InternalExternalSnapshotSwitcher> switchers)) return;
            _seen.Clear();
            foreach (var switcher in switchers)
            {
                if (switcher == null || !switcher.enabled || switcher.trigger == null || !IsClosed(switcher)) continue;
                var interior = InteriorOf(switcher.trigger);
                Closed.Add(new Cab { Trigger = switcher.trigger, InteriorOf = interior != null ? interior.actualTrainCar : null });
            }
            (_interiors, _seen) = (_seen, _interiors);
            // Same rule as for sounds: the game's own camera check ignores the interior frame.
            var camera = PlayerManager.ActiveCamera;
            _listener = camera != null ? ZoneOf(camera.transform) : null;
        }

        private static TrainCarInteriorObject InteriorOf(CameraTrigger trigger)
        {
            if (!_interiors.TryGetValue(trigger, out var interior))
                interior = trigger.GetComponentInParent<TrainCarInteriorObject>();
            _seen[trigger] = interior;
            return interior;
        }

        // The game's IsInside() without the camera part.
        private static bool IsClosed(InternalExternalSnapshotSwitcher switcher)
        {
            if (!(switcher is InternalExternalSnapshotSwitcherDoorsAndWindows)) return true;
            if (WindowsField?.GetValue(switcher) is WindowsBreakingController windows && windows != null && windows.windowsBroken)
                return false;
            if (DoorsField?.GetValue(switcher) is DoorsAndWindowsController doors && doors != null && doors.AnythingOpen())
                return false;
            return true;
        }
    }
}
