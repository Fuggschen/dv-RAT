using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Reads switch damage (0 to 1, by switch ID) from Junction Maintenance (github.com/gercj187/JunctionMaintenance).
    /// A switch under repair cannot be thrown, nor can a fully damaged one when its lock setting is on.
    /// A missing member switches the reader off with one log warning.
    /// </summary>
    public static class JunctionDamage
    {
        // Single-player and multiplayer builds ship the same assembly under two mod IDs.
        private static readonly ModWatch Mod = new ModWatch("JunctionMaintenance", "JunctionMaintenanceMP");

        private const string StoreType = "JunctionMaintenance.DamageStore";
        private const string RepairType = "JunctionMaintenance.JunctionRepair";
        private const string MainType = "JunctionMaintenance.Main";

        // From here the mod counts a switch as fully damaged.
        private const float FullDamage = 0.999f;

        private static bool _searched;
        private static bool _failed;
        private static Func<Dictionary<string, float>> _all;
        private static Func<string, bool> _repairing;
        private static FieldInfo _enabled;
        private static FieldInfo _settings;
        private static FieldInfo _lockAtFullDamage;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Null while the mod is not loaded or could not be read.</summary>
        public static SwitchState Read(Junction junction)
        {
            if (junction == null || !Find()) return null;
            try
            {
                string key = Key(junction);
                _all().TryGetValue(key, out float damage);
                damage = Mathf.Clamp01(damage);
                // Switched off in the mod manager, the mod keeps its damage but lets every switch be thrown.
                bool enabled = (bool)_enabled.GetValue(null);
                bool repairing = enabled && _repairing(key);
                object settings = _settings.GetValue(null);
                bool lockAtFullDamage = settings != null && (bool)_lockAtFullDamage.GetValue(settings);
                return new SwitchState
                {
                    Id = junction.Name(),
                    Damage = damage,
                    Repairing = repairing,
                    Locked = repairing || enabled && lockAtFullDamage && damage >= FullDamage,
                };
            }
            catch (Exception ex)
            {
                Fail($"reading {junction.Name()} failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        /// <summary>The ID the mod files the switch under, as in its DamageStore.MakeKey.</summary>
        private static string Key(Junction junction)
        {
            string id = (junction.junctionData.junctionIdLong ?? "X").Trim();
            return id.Length > 0 ? id : junction.gameObject.name;
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var store = Mod.FindType(StoreType);

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                _all = ModReflection.Method<Func<Dictionary<string, float>>>(store, "All");
                _repairing = ModReflection.Method<Func<string, bool>>(Mod.FindType(RepairType), "IsRunning");
                var main = Mod.FindType(MainType);
                _enabled = main?.GetField("Enabled", flags);
                _settings = main?.GetField("Settings", flags);
                _lockAtFullDamage = _settings?.FieldType.GetField("BlockManualSwitchAtFullDamage", BindingFlags.Public | BindingFlags.Instance);

                string missing = _all == null ? "its damage store"
                    : _repairing == null ? "its running repair"
                    : _enabled == null || _settings == null || _lockAtFullDamage == null ? "its settings"
                    : null;
                if (missing != null)
                {
                    _all = null;
                    Fail($"{missing} was not found");
                    return false;
                }
                RATMain.Log("Junction Maintenance found, its switch damage is shown on the Maintenance page and the Route timetable");
            }
            return _all != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Junction Maintenance: {reason}. Its switch damage is left out.");
        }
    }
}
