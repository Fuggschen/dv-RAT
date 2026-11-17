using System;
using System.Linq;
using System.Reflection;

namespace DVRAT
{
    /// <summary>Reads the Realistic Brakes mod (github.com/Specter242/DVRealisticBrakes) through its RealisticBrakes.Api. A missing member switches the reader off with one log warning.</summary>
    public static class RealisticBrakes
    {
        private static readonly ModWatch Mod = new ModWatch("RealisticBrakes");
        private const string ApiTypeName = "RealisticBrakes.Api";

        public class CarValve
        {
            /// <summary>"G" or "P" for a European valve, "AB" for an American one, or "cut out".</summary>
            public string Text;

            /// <summary>Control reservoir (European) or emergency reservoir (American), gauge bar.</summary>
            public float? ControlBar;

            /// <summary>Null on wagons.</summary>
            public float? EqualizingBar;

            /// <summary>"cut in" or "cut out". Null on wagons.</summary>
            public string DriverValve;
        }

        private static bool _searched;
        private static bool _failed;
        private static PropertyInfo _active;
        private static MethodInfo _pressure, _equipment, _brakeMode, _cutOut, _brakeValveCutOut;

        /// <summary>Active while the API could be read, even if the mod leaves the brakes to the game.</summary>
        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        public static bool Active
        {
            get
            {
                if (!Find()) return false;
                try
                {
                    return _active.GetValue(null) is bool active && active;
                }
                catch (Exception ex)
                {
                    Fail($"its state could not be read: {(ex.InnerException ?? ex).Message}");
                    return false;
                }
            }
        }

        /// <summary>Null when the mod does not run this car's brake.</summary>
        public static CarValve Read(TrainCar car)
        {
            if (car == null || !Active) return null;
            try
            {
                if (!(_equipment.Invoke(null, new object[] { car }) is string equipment)) return null;
                var valve = new CarValve();
                bool american = equipment == "American";
                if (_cutOut.Invoke(null, new object[] { car }) is bool cutOut && cutOut)
                    valve.Text = "cut out";
                else
                    valve.Text = american ? "AB" : (string)_brakeMode.Invoke(null, new object[] { car }) == "Goods" ? "G" : "P";
                valve.ControlBar = Pressure(car, american ? "EmergencyReservoir" : "ControlReservoir");

                if (_brakeValveCutOut.Invoke(null, new object[] { car }) is bool valveCutOut)
                {
                    valve.DriverValve = valveCutOut ? "cut out" : "cut in";
                    valve.EqualizingBar = valveCutOut ? null : Pressure(car, "EqualizingReservoir");
                }
                return valve;
            }
            catch (Exception ex)
            {
                Fail($"reading a valve failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        /// <summary>Gauge bar. The API returns NaN for a missing part, which becomes null.</summary>
        private static float? Pressure(TrainCar car, string part) =>
            _pressure.Invoke(null, new object[] { car, part }) is float bar && !float.IsNaN(bar) ? bar : (float?)null;

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var api = Mod.FindType(ApiTypeName);
                if (api == null)
                {
                    Fail($"{ApiTypeName} was not found");
                    return false;
                }
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                _active = api.GetProperty("Active", flags);
                _pressure = api.GetMethod("Pressure", flags, null, new[] { typeof(TrainCar), typeof(string) }, null);
                var car = new[] { typeof(TrainCar) };
                _equipment = api.GetMethod("Equipment", flags, null, car, null);
                _brakeMode = api.GetMethod("BrakeMode", flags, null, car, null);
                _cutOut = api.GetMethod("CutOut", flags, null, car, null);
                _brakeValveCutOut = api.GetMethod("BrakeValveCutOut", flags, null, car, null);
                if (new MemberInfo[] { _active, _pressure, _equipment, _brakeMode, _cutOut, _brakeValveCutOut }.Any(m => m == null))
                {
                    Fail("its API is older than 1.3.4");
                    return false;
                }
                RATMain.Log("Realistic Brakes found, its valves are shown on the Train and Locomotive pages");
            }
            return _active != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Realistic Brakes: {reason}. Its valves are left out.");
        }
    }
}
