using System;
using System.Collections.Generic;

namespace DVRAT
{
    public class JobData
    {
        /// <summary>Every job the player has taken.</summary>
        public List<JobInfo> AcceptedJobs { get; set; } = new List<JobInfo>();

        /// <summary>Jobs with at least one car in the player's train.</summary>
        public List<JobInfo> CoupledJobs { get; set; } = new List<JobInfo>();

        /// <summary>Jobs marked on the tablet as the ones to do now.</summary>
        public List<JobInfo> ActiveJobs { get; set; } = new List<JobInfo>();

        /// <summary>Marked active on the tablet but still offered by a station, so not taken yet.</summary>
        public List<JobInfo> PlannedJobs { get; set; } = new List<JobInfo>();

        /// <summary>Tons.</summary>
        public float TotalJobMass { get; set; }

        /// <summary>Meters.</summary>
        public float TotalJobLength { get; set; }
    }

    public class JobInfo
    {
        public string JobId { get; set; }

        /// <summary>FreightHaul, ShuntingLoad, LogisticsHaul, etc.</summary>
        public string JobType { get; set; }

        /// <summary>Shunting works inside one station, the other types go between stations.</summary>
        public bool IsShunting => JobType == "ShuntingLoad" || JobType == "ShuntingUnload";

        public string JobTypeDisplay { get; set; }

        /// <summary>FH, LH, SL or SU as on the job booklets, the display name for other types.</summary>
        public string JobTypeShort { get; set; }

        /// <summary>Licenses beyond the job type's own, highest tier only, e.g. "HZ2", "TL1".</summary>
        public List<string> Licenses { get; set; } = new List<string>();

        /// <summary>Includes the license for the job type.</summary>
        public List<string> MissingLicenses { get; set; } = new List<string>();

        public JobState State { get; set; }

        /// <summary>Station/yard ID.</summary>
        public string OriginStation { get; set; }

        /// <summary>Station/yard ID.</summary>
        public string DestinationStation { get; set; }

        public string OriginDisplay { get; set; }

        public string DestinationDisplay { get; set; }

        public string CargoType { get; set; }

        /// <summary>Tons.</summary>
        public float CargoAmount { get; set; }

        public int WagonCount { get; set; }

        /// <summary>Wagons including cargo, in tons.</summary>
        public float TotalMass { get; set; }

        /// <summary>Meters.</summary>
        public float TotalLength { get; set; }

        /// <summary>Without <see cref="Bonus"/>.</summary>
        public float Payment { get; set; }

        /// <summary>For finishing within the time limit, 0 for untimed jobs.</summary>
        public float Bonus { get; set; }

        /// <summary>Null if untimed.</summary>
        public TimeSpan? TimeLimit { get; set; }

        /// <summary>Negative once the limit ran out. Null if untimed.</summary>
        public TimeSpan? TimeLeft { get; set; }

        public bool IsCoupled { get; set; }

        public List<string> WagonIds { get; set; } = new List<string>();

        /// <summary>Display ID of the track the first remaining transport starts from.</summary>
        public string StartTrackId { get; set; }

        /// <summary>Display ID of the track the last remaining transport ends on.</summary>
        public string DestinationTrackId { get; set; }

        /// <summary>Marked as active on the tablet.</summary>
        public bool IsActive { get; set; }

        /// <summary>One line per task in job order, e.g. "2 cars HB-1S to HB-7L".</summary>
        public List<string> Steps { get; set; } = new List<string>();

        public List<JobCar> Cars { get; set; } = new List<JobCar>();
    }

    public class JobCar
    {
        /// <summary>As printed on the car, e.g. "BXK123".</summary>
        public string Id { get; set; }
        public string CarType { get; set; }
        /// <summary>The livery's name in the game's language.</summary>
        public string CarName { get; set; }

        /// <summary>Display ID, null while the car moves between tracks.</summary>
        public string Track { get; set; }

        /// <summary>Coupler to coupler, in meters.</summary>
        public float Length { get; set; }

        public bool IsCoupled { get; set; }

        /// <summary>In job order, finished tasks left out.</summary>
        public List<JobLeg> Legs { get; set; } = new List<JobLeg>();

        /// <summary>E.g. "Unload at HB-WH1", "Done" once finished.</summary>
        public string NextTask
        {
            get
            {
                if (Legs.Count == 0) return "Done";
                var leg = Legs[0];
                switch (leg.Kind)
                {
                    case JobLegKind.Load: return $"Load at {leg.ToTrack}";
                    case JobLegKind.Unload: return $"Unload at {leg.ToTrack}";
                    default: return $"Move to {leg.ToTrack}";
                }
            }
        }

        /// <summary>Null once finished.</summary>
        public string FinalTrack => Legs.Count > 0 ? Legs[Legs.Count - 1].ToTrack : null;
    }

    public enum JobLegKind
    {
        Transport,
        /// <summary>At the warehouse on <see cref="JobLeg.ToTrack"/>.</summary>
        Load,
        /// <summary>At the warehouse on <see cref="JobLeg.ToTrack"/>.</summary>
        Unload,
    }

    public class JobLeg
    {
        public JobLegKind Kind { get; set; }
        public string FromTrack { get; set; }
        public string ToTrack { get; set; }

        /// <summary>Boarding or alighting at a Passenger Jobs stop, with the cars still coupled.</summary>
        public bool Passengers { get; set; }
    }

    public enum JobState
    {
        Available,
        Active,
        Completed,
        Failed,
        Expired
    }
}