using System;
using System.Collections.Generic;
using System.Reflection;

namespace DVRAT
{
    /// <summary>Reads the Realistic Firing mod (github.com/Specter242/DVRealisticFiring) through its RealisticFiring.Api. A missing member or a changed API version switches the reader off with one log warning.</summary>
    public static class RealisticFiring
    {
        private static readonly ModWatch Mod = new ModWatch("RealisticFiring");
        private const string ApiTypeName = "RealisticFiring.Api";

        // Api.Version 1: the readings' names, units and meanings this reader expects. New readings keep the version.
        private const int ApiVersion = 1;

        private static bool _searched;
        private static bool _failed;
        private static Func<TrainCar, bool> _has;
        private static Func<TrainCar, IDictionary<string, object>> _read;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        public static bool Active => Find();

        /// <summary>The mod's readings of the car's fire, boiler and engine, as documented on RealisticFiring.Api.Read. Null when the mod does not fire this car, e.g. a tender or a diesel.</summary>
        public static IDictionary<string, object> Read(TrainCar car)
        {
            if (car == null || !Find()) return null;
            try
            {
                return _has(car) ? _read(car) : null;
            }
            catch (Exception ex)
            {
                Fail($"reading a fire failed: {ex.Message}");
                return null;
            }
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var api = Mod.FindType(ApiTypeName);
                if (api == null)
                {
                    Fail($"{ApiTypeName} was not found, it came with 1.24.2");
                    return false;
                }
                var version = api.GetField("Version", BindingFlags.Public | BindingFlags.Static);
                if (!(version?.GetRawConstantValue() is int found) || found != ApiVersion)
                {
                    Fail($"its API version is {version?.GetRawConstantValue() ?? "missing"}, the tablet reads version {ApiVersion}");
                    return false;
                }
                _has = ModReflection.Method<Func<TrainCar, bool>>(api, "Has");
                _read = ModReflection.Method<Func<TrainCar, IDictionary<string, object>>>(api, "Read");
                if (_has == null || _read == null)
                {
                    Fail("its API lacks Has or Read");
                    return false;
                }
                RATMain.Log("Realistic Firing found, its readings are shown on the Firing page");
            }
            return _read != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Realistic Firing: {reason}. Its readings are left out.");
        }
    }
}
