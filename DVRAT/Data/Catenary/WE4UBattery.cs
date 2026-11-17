using System;
using System.Reflection;

namespace DVRAT
{
    /// <summary>Traction battery of Comrad501's WE-4U. Read from the private power circuit of its eu07_driver by reflection, as no port exists. A missing member switches this off with one warning.</summary>
    internal static class WE4UBattery
    {
        private static readonly ModWatch Mod = new ModWatch("EU07Electrics");
        private const string DriverType = "electric_sim.unit_A.eu07_driver";
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static bool _searched, _failed;
        private static Type _driver;
        private static FieldInfo _power;
        private static PropertyInfo _active, _wanted, _volts, _amps, _charge;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Null for a car that is not a WE-4U, or while the mod is off.</summary>
        public static TractionBattery Read(TrainCar car)
        {
            if (car == null || !Find()) return null;
            try
            {
                var driver = car.GetComponent(_driver);
                object power = driver == null ? null : _power.GetValue(driver);
                if (power == null) return null;
                return new TractionBattery
                {
                    Active = (bool)_active.GetValue(power),
                    Wanted = (bool)_wanted.GetValue(power),
                    Voltage = (float)_volts.GetValue(power),
                    Current = (float)_amps.GetValue(power),
                    Charge = (float)_charge.GetValue(power),
                };
            }
            catch (Exception ex)
            {
                Fail($"reading {car.ID} failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (_searched) return _power != null;
            _searched = true;

            _driver = Mod.FindType(DriverType);
            _power = _driver?.GetField("_power", Instance);
            var circuit = _power?.FieldType;
            _active = circuit?.GetProperty("battery_contactor", Instance);
            _wanted = circuit?.GetProperty("battery_wanted", Instance);
            _volts = circuit?.GetProperty("battery_volts", Instance);
            _amps = circuit?.GetProperty("battery_amps", Instance);
            _charge = circuit?.GetProperty("battery_charge", Instance);
            if (_power == null || _active == null || _wanted == null || _volts == null || _amps == null || _charge == null)
            {
                _power = null;
                Fail("its traction battery was not found");
                return false;
            }
            RATMain.Log("WE-4U found, its traction battery is shown on the Electric page");
            return true;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"WE-4U: {reason}. Its traction battery is left out.");
        }
    }
}
