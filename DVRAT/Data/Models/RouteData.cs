using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>The route ahead of the player's car. All distances are meters from the car.</summary>
    public class RouteData
    {
        public string CurrentTrackId { get; set; }
        public string CurrentTrackName { get; set; }

        /// <summary>"forward" or "backward" relative to the car.</summary>
        public string Direction { get; set; }

        public float SpeedLimitKmh { get; set; }

        /// <summary>Positive uphill, in percent.</summary>
        public float CurrentGradePercent { get; set; }

        /// <summary>Where the scan stopped: its range, the end of track or the end of the planned route.</summary>
        public float ScannedMeters { get; set; }

        /// <summary>Yard ID, null when no route is planned.</summary>
        public string PlannedDestination { get; set; }

        public List<RouteSection> SpeedSections { get; set; } = new List<RouteSection>();

        /// <summary>Track Maintenance damage that lowers the limit, nearest first. Already part of <see cref="SpeedSections"/>.</summary>
        public List<SpeedRestriction> Restrictions { get; set; } = new List<SpeedRestriction>();

        /// <summary>At negative positions.</summary>
        public List<RouteSection> SpeedSectionsBehind { get; set; } = new List<RouteSection>();

        /// <summary>Nearest last, at negative distances.</summary>
        public List<TrackEvent> PassedEvents { get; set; } = new List<TrackEvent>();

        /// <summary>Percent, positive uphill.</summary>
        public List<RouteSection> GradeSections { get; set; } = new List<RouteSection>();

        /// <summary>Sorted by distance.</summary>
        public List<TrackEvent> UpcomingEvents { get; set; } = new List<TrackEvent>();

        public float DistanceToNextStation { get; set; }

        /// <summary>Yard ID.</summary>
        public string NextStationName { get; set; }
    }

    public class TrackEvent
    {
        public TrackEventType EventType { get; set; }
        public string Name { get; set; }
        public float DistanceMeters { get; set; }

        /// <summary>New limit in km/h, grade in percent, or branch index, depending on the type.</summary>
        public float Value { get; set; }

        public string StationId { get; set; }

        /// <summary>Stations: display ID of the track the route leads onto, e.g. "HB-D7L".</summary>
        public string Track { get; set; }

        /// <summary>Signals: aspect as the signal pack names it, null while the signal is off.</summary>
        public string Aspect { get; set; }

        /// <summary>Signals: the aspect does not allow passing.</summary>
        public bool Restrictive { get; set; }

        /// <summary>Switches: set to another branch than the planned route takes.</summary>
        public bool Misaligned { get; set; }

        /// <summary>Switches: the route enters from a branch. Listed only while Junction Maintenance has it damaged or locked.</summary>
        public bool Trailing { get; set; }

        /// <summary>Switches: Junction Maintenance damage and whether it can be thrown. Null without the mod.</summary>
        public SwitchState Switch { get; set; }
    }

    public enum TrackEventType
    {
        Station,
        SpeedLimitChange,
        GradeChange,
        Junction,
        Signal,
        /// <summary>The route enters a named station track.</summary>
        Track,
        EndOfTrack,
        /// <summary>The train's end passes a raised limit. Added by the timetable.</summary>
        TrainClear,
        /// <summary>1500 V DC Catenary neutral section: no power until its end.</summary>
        NeutralBegin,
        NeutralEnd,
        /// <summary>Track Maintenance damage begins. Value is the limit from there, 0 on closed track.</summary>
        TrackDamage,
        /// <summary>Value is the limit from there, 0 when no sign gives one.</summary>
        TrackDamageEnd,
        /// <summary>Catenary wire, overhead or side rail, begins after a gap.</summary>
        WireBegin,
        /// <summary>No power until the wire begins again.</summary>
        WireEnd,
    }
}
