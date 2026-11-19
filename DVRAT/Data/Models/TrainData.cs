using System.Collections.Generic;

namespace DVRAT
{
    public class TrainData
    {
        /// <summary>Tons.</summary>
        public float TotalMass { get; set; }

        /// <summary>Meters.</summary>
        public float TotalLength { get; set; }

        public float SpeedKmh { get; set; }

        /// <summary>Above sea level in meters, as dv-hud measures it.</summary>
        public float AltitudeM { get; set; }

        /// <summary>Slope under the tablet's car, positive uphill in its direction of travel, in percent.</summary>
        public float PitchGradePercent { get; set; }

        /// <summary>The Realistic Brakes mod runs the brakes, so the cars have <see cref="CarInfo.BrakeValve"/>.</summary>
        public bool RealisticBrakes { get; set; }

        /// <summary>Coupled cars excluding locomotives.</summary>
        public int WagonCount { get; set; }

        /// <summary>In train order, the end with a loco first.</summary>
        public List<CarInfo> Cars { get; set; } = new List<CarInfo>();

        public LocoInfo Locomotive { get; set; }

        public BrakingInfo Braking { get; set; } = new BrakingInfo();

        /// <summary>True while the Solar Gadgets mod is loaded.</summary>
        public bool SolarGadgets { get; set; }

        /// <summary>Cars with a Solar Gadgets network, in train order.</summary>
        public List<CarPower> Power { get; set; } = new List<CarPower>();

        /// <summary>Null for a loco that does not run on the overhead wire.</summary>
        public ElectricInfo Electric { get; set; }
    }

    /// <summary>Stopping distance = v² / (2 × deceleration).</summary>
    public class BrakingInfo
    {
        /// <summary>Meters, at the current brake setting.</summary>
        public float CurrentBrakingDistance { get; set; }

        /// <summary>Meters, full emergency braking.</summary>
        public float EmergencyBrakingDistance { get; set; }

        /// <summary>In m/s² at the current brake state.</summary>
        public float DecelerationRate { get; set; }

        /// <summary>In m/s² at full emergency brake.</summary>
        public float MaxDecelerationRate { get; set; }

        /// <summary>In m/s.</summary>
        public float SpeedMs { get; set; }

        /// <summary>0-1, reduced by damage or worn brakes.</summary>
        public float BrakeEffectiveness { get; set; }
    }

    public class CarInfo
    {
        /// <summary>For example "Loco-001" or "Freight-012".</summary>
        public string CarId { get; set; }

        /// <summary>Livery ID, e.g. "LocoDE2" or "FlatbedStakes".</summary>
        public string CarType { get; set; }

        /// <summary>The livery's name in the game's language, as the vehicle catalogue shows it.</summary>
        public string CarName { get; set; }

        public bool IsLocomotive { get; set; }

        /// <summary>Tons, cargo included.</summary>
        public float Mass { get; set; }

        /// <summary>Meters.</summary>
        public float Length { get; set; }

        /// <summary>Empty string if none.</summary>
        public string LoadedCargo { get; set; }

        /// <summary>Tons.</summary>
        public float CargoAmount { get; set; }

        public bool IsDerailed { get; set; }

        /// <summary>0 = perfect, 100 = destroyed.</summary>
        public float DamagePercent { get; set; }

        public string CurrentTrackId { get; set; }

        // Brake pressures in bar above atmosphere, as the gauges show them.
        public float BrakePipePressure { get; set; }
        /// <summary>Auxiliary reservoir of a wagon, main reservoir for a loco.</summary>
        public float ReservoirPressure { get; set; }
        public float BrakeCylinderPressure { get; set; }
        /// <summary>With Realistic Brakes, an American valve's emergency reservoir.</summary>
        public float ControlReservoirPressure { get; set; }
        /// <summary>0 to 1.</summary>
        public float BrakingFactor { get; set; }
        /// <summary>In °C. From 600 °C the brakes overheat and lose force, at 1000 °C most of it.</summary>
        public float BrakeTemperature { get; set; }
        /// <summary>Realistic Brakes valve setting and state, e.g. "G empty on". Null without the mod.</summary>
        public string BrakeValve { get; set; }
        public bool HandbrakeApplied { get; set; }

        /// <summary>100 at the point the car derails.</summary>
        public float DerailPercent { get; set; }

        /// <summary>In kN, with the car before it in <see cref="TrainData.Cars"/>. Null for the first car.</summary>
        public float? CouplerForceKn { get; set; }

        /// <summary>Taken or not. Null for cars of no job.</summary>
        public string JobId { get; set; }
        public bool JobTaken { get; set; }

        /// <summary>Display ID of the track the car's job sends it to next, null when there is none.</summary>
        public string NextDestination { get; set; }

        /// <summary>The car stands on <see cref="NextDestination"/>.</summary>
        public bool AtDestination { get; set; }
    }

    public class LocoInfo : CarInfo
    {
        public LocoInfo()
        {
            IsLocomotive = true;
        }

        public float TrainBrakePosition { get; set; }      // 0.0 - 1.0
        public float IndependentBrakePosition { get; set; } // 0.0 - 1.0
        public float HandbrakePosition { get; set; }        // 0.0 - 1.0
        public float MainReservoirPressure { get; set; }    // bar above atmosphere
        /// <summary>Realistic Brakes: pressure the driver's brake valve sets the pipe to. Null without it.</summary>
        public float? EqualizingReservoirPressure { get; set; }
        /// <summary>Realistic Brakes: driver's brake valve position, e.g. "Running". Null without it.</summary>
        public string DriverValve { get; set; }

        public bool HasTraction { get; set; }
        public float TractiveEffortKn { get; set; }
        public float PowerKw { get; set; }
        /// <summary>Most force the wheels can pass to the rail before they slip.</summary>
        public float AdhesionLimitKn { get; set; }
        public float Wheelslip { get; set; }    // 0.0 - 1.0
        public float WheelSlide { get; set; }   // 0.0 - 1.0, braked wheels sliding
        public bool HasCutoff { get; set; }
        public float Cutoff { get; set; }       // 0.0 - 1.0

        public float Throttle { get; set; }     // 0.0 - 1.0
        public float Reverser { get; set; }     // -1.0 (reverse) to 1.0 (forward), 0.0 neutral

        public bool HasEngine { get; set; }
        public bool EngineRunning { get; set; }
        public float EngineRPM { get; set; }

        /// <summary>Run up since the last service, tax included, as the career manager lists it. Null for a loco nobody charges for, e.g. in sandbox.</summary>
        public float? Fee { get; set; }

        /// <summary>The player's own loco: the fee is paid at service, not at the career manager.</summary>
        public bool FeeIsService { get; set; }

        /// <summary>Distance the loco has run in this savegame, see <see cref="Odometer"/>. Null on a client before the host sent it.</summary>
        public double? OdometerKm { get; set; }

        /// <summary>In °C. Null without traction motors.</summary>
        public float? TmTemperature { get; set; }

        /// <summary>Above this the motors can burn out. Null when unknown.</summary>
        public float? TmOverheatTemperature { get; set; }

        /// <summary>Null for a loco without a damage controller.</summary>
        public DamageInfo Damage { get; set; }

        /// <summary>False without a temperature port outside the boiler, firebox and traction motors.</summary>
        public bool HasTemperature { get; set; }
        public float Temperature { get; set; }  // °C

        public bool HasBoiler { get; set; }
        public float BoilerPressure { get; set; }   // bar
        public float BoilerWaterLevel { get; set; } // 0.0 - 1.0
        public bool HasFirebox { get; set; }
        public bool FireOn { get; set; }
        public float FireboxTemperature { get; set; } // °C

        /// <summary>Fuel, oil, sand, water, coal and charge, keyed by those names in lower case.</summary>
        public Dictionary<string, ResourceLevel> Resources { get; set; } = new Dictionary<string, ResourceLevel>();

        /// <summary>Named as the game does ("component.PORT").</summary>
        public List<SimPort> Ports { get; set; } = new List<SimPort>();
    }

    public class ResourceLevel
    {
        public float Amount { get; set; }
        public float Capacity { get; set; }
        public float Normalized { get; set; }   // 0.0 - 1.0

        /// <summary>Used per minute since the last reading, see <see cref="ResourceRates"/>. Null while the level rises or without an earlier reading.</summary>
        public float? PerMinute { get; set; }
    }

    public class SimPort
    {
        public string Id { get; set; }

        /// <summary>E.g. PRESSURE or RPM.</summary>
        public string Type { get; set; }

        public float Value { get; set; }
    }
}