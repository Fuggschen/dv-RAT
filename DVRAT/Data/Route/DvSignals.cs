using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DVRAT
{
    /// <summary>
    /// Reads signals of the DV Signals mod (github.com/WhistleWiz/dv-signals) through reflection.
    /// A missing member, e.g. after a mod update, switches the reader off with one log warning.
    /// </summary>
    public static class DvSignals
    {
        private static readonly ModWatch Mod = new ModWatch("DVSignals");
        private const string ManagerTypeName = "Signals.Game.SignalManager";

        public class Placed
        {
            public string Name;

            /// <summary>Meters from the track's in end.</summary>
            public float Span;

            /// <summary>Guards trains running from the in end to the out end.</summary>
            public bool GuardsForward;

            /// <summary>Main, buffer stop or turntable, from the controller's type name.</summary>
            public string Kind;

            /// <summary>Kept to read the aspect, which changes.</summary>
            public object Controller;
        }

        private static bool _searched;
        private static bool _failed;
        private static Type _managerType;
        private static readonly Dictionary<(Type, string), MemberInfo> Members = new Dictionary<(Type, string), MemberInfo>();

        public static bool Available => Manager() != null;

        // Signals are placed once per world, so positions are cached per manager. Rereading took about 5 ms.
        private static object _cachedManager;
        private static Dictionary<RailTrack, List<Placed>> _cachedByTrack;

        /// <summary>Empty when the mod is missing or not running.</summary>
        public static Dictionary<RailTrack, List<Placed>> ByTrack()
        {
            var manager = Manager();
            if (manager == null) return new Dictionary<RailTrack, List<Placed>>();
            if (ReferenceEquals(manager, _cachedManager)) return _cachedByTrack;

            var result = Read(manager);
            // Not cached while empty: the manager may run before it has placed its signals.
            if (result.Count > 0)
            {
                _cachedManager = manager;
                _cachedByTrack = result;
            }
            return result;
        }

        private static Dictionary<RailTrack, List<Placed>> Read(object manager)
        {
            var result = new Dictionary<RailTrack, List<Placed>>();
            try
            {
                foreach (var controller in (IEnumerable)Get(manager, "AllControllers") ?? new object[0])
                {
                    // Shunting signals only matter in yards, distant signals only repeat the main signal.
                    if (controller == null || Get(controller, "Type")?.ToString() == "Shunting"
                        || controller.GetType().Name.Contains("Distant")) continue;
                    var placement = Get(controller, "PlacementInfo");
                    if (!(Get(placement, "Track") is RailTrack track)) continue;

                    if (!result.TryGetValue(track, out var list))
                        result[track] = list = new List<Placed>();
                    list.Add(new Placed
                    {
                        Name = Get(controller, "Name") as string ?? "",
                        Span = Convert.ToSingle(Get(placement, "Span")),
                        // Direction points away from what the signal protects, so trains it applies to run the other way.
                        GuardsForward = Get(placement, "Direction")?.ToString() == "In",
                        Kind = KindOf(controller.GetType().Name),
                        Controller = controller,
                    });
                }
            }
            catch (Exception ex)
            {
                Fail($"reading the signals failed: {ex.Message}");
                result.Clear();
            }
            return result;
        }

        /// <summary>Null while the signal is off.</summary>
        public static (string id, bool restrictive)? Aspect(Placed signal)
        {
            if (_failed) return null;
            try
            {
                var shown = Call(signal.Controller, "GetControllerSignal");
                var aspect = shown != null ? Get(shown, "CurrentAspect") : null;
                if (aspect == null) return null;
                return (Get(aspect, "Id") as string ?? "", Get(aspect, "DisallowPassing") is bool stop && stop);
            }
            catch (Exception ex)
            {
                Fail($"reading an aspect failed: {ex.Message}");
                return null;
            }
        }

        private static string KindOf(string typeName)
        {
            if (typeName.Contains("BufferStop")) return "Buffer stop signal";
            if (typeName.Contains("Turntable")) return "Turntable signal";
            return "Signal";
        }

        private static object Manager()
        {
            if (_failed || !Mod.Active) return null;
            if (!_searched)
            {
                _searched = true;
                _managerType = Mod.FindType(ManagerTypeName);
                if (_managerType == null)
                {
                    Fail($"{ManagerTypeName} was not found");
                    return null;
                }
                RATMain.Log("DV Signals found, signals are shown in the timetable");
            }
            if (_managerType == null) return null;

            try
            {
                // Reading Instance before the mod runs makes the game create an empty manager.
                if (!(GetStatic("Running") is bool running) || !running) return null;
                return GetStatic("Instance");
            }
            catch (Exception ex)
            {
                Fail($"its signal manager could not be read: {ex.Message}");
                return null;
            }
        }

        private static object GetStatic(string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var property = _managerType.GetProperty(name, flags);
            if (property != null) return property.GetValue(null);
            var field = _managerType.GetField(name, flags);
            if (field != null) return field.GetValue(null);
            throw new MissingMemberException(_managerType.Name, name);
        }

        /// <summary>Public property or field, null for a null target.</summary>
        private static object Get(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            if (!Members.TryGetValue((type, name), out var member))
            {
                member = (MemberInfo)type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                    ?? type.GetField(name, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new MissingMemberException(type.Name, name);
                Members[(type, name)] = member;
            }
            return member is PropertyInfo property ? property.GetValue(target) : ((FieldInfo)member).GetValue(target);
        }

        private static object Call(object target, string name)
        {
            var type = target.GetType();
            if (!Members.TryGetValue((type, name), out var member))
            {
                member = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? throw new MissingMethodException(type.Name, name);
                Members[(type, name)] = member;
            }
            return ((MethodInfo)member).Invoke(target, null);
        }

        private static void Fail(string reason)
        {
            _failed = true;
            _cachedManager = null;
            _cachedByTrack = null;
            RATMain.LogWarning($"DV Signals: {reason}. Signals are left out of the timetable.");
        }
    }
}
