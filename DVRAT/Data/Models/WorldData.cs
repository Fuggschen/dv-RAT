using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Game time, weather, money and licenses, plus the station the player is nearest to.</summary>
    public class WorldData
    {
        /// <summary>Null while the weather system is not loaded.</summary>
        public DateTime? GameTime { get; set; }
        public bool IsDay { get; set; }

        /// <summary>Game clock seconds per real second.</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>0.0 - 1.0.</summary>
        public float Rain { get; set; }

        /// <summary>0.0 - 1.0. Rails stay wet for a while after the rain stops.</summary>
        public float Wetness { get; set; }

        /// <summary>Starts at 06:00. Empty until the game has one.</summary>
        public List<ForecastSlot> Forecast { get; set; } = new List<ForecastSlot>();

        public bool SeasonsInstalled { get; set; }

        /// <summary>Every other mod the tablet can read, loaded or not.</summary>
        public List<IntegrationStatus> Integrations { get; set; } = new List<IntegrationStatus>();

        /// <summary>Null until DVSeasons knows the temperature.</summary>
        public SeasonsData Seasons { get; set; }

        public double Money { get; set; }

        /// <summary>Unpaid, highest first.</summary>
        public List<Fee> Fees { get; set; } = new List<Fee>();

        /// <summary>False when the fees were not read, so an empty <see cref="Fees"/> means nothing.</summary>
        public bool FeesRead { get; set; }

        /// <summary>Most expensive repair first.</summary>
        public List<OwnedCar> OwnedCars { get; set; } = new List<OwnedCar>();

        /// <summary>Job licenses first.</summary>
        public List<License> Licenses { get; set; } = new List<License>();

        /// <summary>The station picked on the Station page, else the nearest. Null when no station is loaded.</summary>
        public string NearestStationId { get; set; }

        /// <summary>Meters from the player to the station's centre.</summary>
        public float NearestStationDistance { get; set; }

        /// <summary>Picked on the Station page rather than the nearest.</summary>
        public bool StationPicked { get; set; }

        /// <summary>Inside the area where the station offers its jobs.</summary>
        public bool AtStation { get; set; }

        /// <summary>True while the player is there, or anywhere with Persistent Jobs, as the game rerolls offers on each visit.</summary>
        public bool JobsKnown { get; set; }

        /// <summary>Filled while <see cref="JobsKnown"/>.</summary>
        public List<JobInfo> StationJobs { get; set; } = new List<JobInfo>();

        public List<StationEntry> Stations { get; set; } = new List<StationEntry>();

        /// <summary>Sorted by ID.</summary>
        public List<TrackSpace> StationTracks { get; set; } = new List<TrackSpace>();

        /// <summary>True while the Junction Maintenance mod is loaded and read.</summary>
        public bool SwitchesKnown { get; set; }

        /// <summary>Most damaged first. Filled while <see cref="SwitchesKnown"/>.</summary>
        public List<SwitchState> StationSwitches { get; set; } = new List<SwitchState>();

        /// <summary>True while the Track Maintenance mod is loaded and read.</summary>
        public bool TrackDamageKnown { get; set; }

        /// <summary>Tracks and switches, most damaged first. Filled while <see cref="TrackDamageKnown"/>.</summary>
        public List<DamagedSection> StationTrackDamage { get; set; } = new List<DamagedSection>();
    }

    /// <summary>A stretch of track as Track Maintenance rates it for repair.</summary>
    public class DamagedSection
    {
        /// <summary>E.g. "HB-D7L". For the tracks of a switch, the switch's ID.</summary>
        public string Track { get; set; }

        /// <summary>Meters from the track's start.</summary>
        public float From { get; set; }
        public float To { get; set; }

        /// <summary>1 - 100.</summary>
        public int DamagePercent { get; set; }

        /// <summary>Speed the mod advises. Null for none, 0 for a closed track.</summary>
        public int? LimitKmh { get; set; }
    }

    /// <summary>A switch as the Junction Maintenance mod keeps it.</summary>
    public class SwitchState
    {
        /// <summary>E.g. "S-0228-HB".</summary>
        public string Id { get; set; }

        /// <summary>0.0 - 1.0. The higher, the likelier it throws itself under a train from its point end.</summary>
        public float Damage { get; set; }

        public bool Repairing { get; set; }

        /// <summary>Not throwable by hand: under repair, or fully damaged while the mod's setting locks such switches.</summary>
        public bool Locked { get; set; }

        /// <summary>Whole percent as the mod lists it, so any damage left shows as 1 at least.</summary>
        public int DamagePercent => Damage > 0.0001f ? Math.Max(1, (int)Math.Round(Damage * 100f)) : 0;
    }

    public class License
    {
        public string Name { get; set; }

        /// <summary>"job" or "general".</summary>
        public string Kind { get; set; }
    }

    public class Fee
    {
        /// <summary>Job, car or "other", as the career manager names it.</summary>
        public string Id { get; set; }

        /// <summary>"job", "loco", "owned" or "other".</summary>
        public string Kind { get; set; }

        public float Price { get; set; }

        /// <summary>False while it cannot be paid yet, e.g. for a job in progress.</summary>
        public bool Payable { get; set; }
    }

    public class OwnedCar
    {
        public string Id { get; set; }

        /// <summary>Livery ID, empty for a destroyed car.</summary>
        public string Type { get; set; }

        /// <summary>In the game's language, empty for a destroyed car.</summary>
        public string Name { get; set; }

        public float ServicePrice { get; set; }

        public bool Destroyed { get; set; }

        /// <summary>Display ID, null while not on a track.</summary>
        public string Track { get; set; }

        /// <summary>Meters from the player.</summary>
        public float Distance { get; set; }
    }

    public class StationEntry
    {
        public string Id { get; set; }

        /// <summary>Meters from the player to the station's centre.</summary>
        public float Distance { get; set; }
    }

    public class TrackSpace
    {
        public string Id { get; set; }
        public float Length { get; set; }

        /// <summary>Length not taken by cars.</summary>
        public float Free { get; set; }

        /// <summary>Sub-yard, e.g. "C" for HB-C3L.</summary>
        public string Group { get; set; } = "";

        /// <summary>The rail length that car positions are counted along.</summary>
        public float RailLength { get; set; }

        /// <summary>In order from the end drawn on the left.</summary>
        public List<TrackCar> Cars { get; set; } = new List<TrackCar>();
    }

    public enum TrackCarKind
    {
        Other,
        /// <summary>Not in the tablet's train.</summary>
        Loco,
        /// <summary>Belongs to a job marked for the shunting planner.</summary>
        Planned,
        /// <summary>In the tablet's train.</summary>
        Train,
    }

    /// <summary>A car on a station track, in meters from the end of the track drawn on the left.</summary>
    public class TrackCar
    {
        public string Id { get; set; }

        /// <summary>In the game's language.</summary>
        public string Name { get; set; }

        /// <summary>In the game's language, null when empty.</summary>
        public string Cargo { get; set; }

        /// <summary>Offered or taken. Null without a job.</summary>
        public string JobId { get; set; }

        public float From { get; set; }
        public float To { get; set; }
        public TrackCarKind Kind { get; set; }

        /// <summary>
        /// Centred on the bogies, or on the one bogie on this track over a switch, cut off at the track's ends.
        /// <paramref name="flip"/> counts from the out end so all tracks of a yard are drawn the same way round.
        /// </summary>
        /// <param name="bogieA">Span of a bogie on this track, null when it is on another track.</param>
        public static (float from, float to) Extent(float? bogieA, float? bogieB, float carLength, float railLength, bool flip)
        {
            float centre = bogieA.HasValue && bogieB.HasValue ? (bogieA.Value + bogieB.Value) / 2f : bogieA ?? bogieB ?? 0f;
            float from = Math.Max(0f, centre - carLength / 2f);
            float to = Math.Min(railLength, centre + carLength / 2f);
            return flip ? (railLength - to, railLength - from) : (from, to);
        }
    }
}
