using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Reads track damage from Track Maintenance (github.com/gercj187/TrackMaintenance). Sections of a set length from the track's start get a speed limit by damage, which only its measuring run enforces.
    /// A missing member switches the reader off with one log warning.
    /// </summary>
    public static class TrackDamage
    {
        // Single-player and multiplayer builds ship the same assembly under two mod IDs.
        private static readonly ModWatch Mod = new ModWatch("TrackMaintenance", "TrackMaintenanceMP");

        private const string LedgerType = "TrackMaintenance.DamageLedger";
        private const string LimitsType = "TrackMaintenance.SpeedLimits";
        private const string MainType = "TrackMaintenance.Main";

        // SpeedLimits.ForPercent returns this for a section without a limit.
        private const int NoLimit = -1;

        private static bool _searched;
        private static bool _failed;
        private static Func<RailTrack, bool> _has;
        private static Func<RailTrack, double, double, float> _section;
        private static Func<int, int> _limit;
        private static FieldInfo _settings;
        private static PropertyInfo _sectionLength;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Damaged sections as in the mod's repair list, in m from the track's start. Empty when undamaged or the mod is unavailable.</summary>
        public static List<DamagedSection> On(RailTrack track)
        {
            var sections = new List<DamagedSection>();
            if (track == null || !Find()) return sections;
            try
            {
                if (!_has(track)) return sections;
                object settings = _settings.GetValue(null);
                if (settings == null) return sections;

                double length = (float)_sectionLength.GetValue(settings);
                double span = track.GetKinkedPointSet().span;
                for (int k = 0; k * length < span; k++)
                {
                    double from = k * length;
                    double to = Math.Min(span, from + length);
                    int percent = Mathf.RoundToInt(_section(track, from, to) * 100f);
                    if (percent < 1) continue;
                    int limit = _limit(percent);
                    sections.Add(new DamagedSection
                    {
                        From = (float)from,
                        To = (float)to,
                        DamagePercent = percent,
                        LimitKmh = limit == NoLimit ? (int?)null : limit,
                    });
                }
            }
            catch (Exception ex)
            {
                Fail($"reading {track.name} failed: {(ex.InnerException ?? ex).Message}");
                sections.Clear();
            }
            return sections;
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (!_searched)
            {
                _searched = true;
                var ledger = Mod.FindType(LedgerType);

                _has = ModReflection.Method<Func<RailTrack, bool>>(ledger, "Has");
                _section = ModReflection.Method<Func<RailTrack, double, double, float>>(ledger, "Section01");
                _limit = ModReflection.Method<Func<int, int>>(Mod.FindType(LimitsType), "ForPercent");
                _settings = Mod.FindType(MainType)?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
                _sectionLength = _settings?.FieldType.GetProperty("RepairSectionLength", BindingFlags.Public | BindingFlags.Instance);

                string missing = _has == null || _section == null ? "its damage ledger"
                    : _limit == null ? "its speed limits"
                    : _settings == null || _sectionLength == null ? "its section length setting"
                    : null;
                if (missing != null)
                {
                    _has = null;
                    Fail($"{missing} was not found");
                    return false;
                }
                RATMain.Log("Track Maintenance found, its track damage is shown on the Maintenance page and the Route timetable");
            }
            return _has != null;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            RATMain.LogWarning($"Track Maintenance: {reason}. Its track damage is left out.");
        }
    }
}
