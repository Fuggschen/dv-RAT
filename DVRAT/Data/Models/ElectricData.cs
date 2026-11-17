using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Draw from the overhead wire of the 1500 V DC Catenary mod. V and A, negative current while regenerating.</summary>
    public class ElectricInfo
    {
        /// <summary>For the 2WE3 always unit A.</summary>
        public string CarId { get; set; }

        /// <summary>"Catenary for CCL" or "2WE3".</summary>
        public string Source { get; set; }

        public float LineVoltage { get; set; }

        /// <summary>Null when the loco does not report it.</summary>
        public float? NominalVoltage { get; set; }

        public float LineCurrent { get; set; }

        public float PowerKw => LineVoltage * LineCurrent / 1000f;

        /// <summary>False when the loco's pantographs cannot be read.</summary>
        public bool PantographsReported { get; set; }

        public List<PantographState> Pantographs { get; set; } = new List<PantographState>();

        /// <summary>In °C.</summary>
        public float? MotorTemperature { get; set; }

        /// <summary>Motors that are not burnt out.</summary>
        public int? WorkingMotors { get; set; }

        /// <summary>Meter reading since fees were last paid. Regenerative braking counts back down.</summary>
        public float? EnergyUnits { get; set; }

        public float? KwhPerUnit { get; set; }

        public float? EnergyKwh => EnergyUnits * KwhPerUnit;

        /// <summary>Null for a loco without one.</summary>
        public TractionBattery Battery { get; set; }
    }

    /// <summary>Feeds the traction motors where there is no wire, e.g. on the WE-4U.</summary>
    public class TractionBattery
    {
        /// <summary>The battery feeds the motors instead of the line.</summary>
        public bool Active { get; set; }

        /// <summary>Switch is on, but the loco's circuit may not have handed over yet.</summary>
        public bool Wanted { get; set; }

        public float Voltage { get; set; }

        /// <summary>In A.</summary>
        public float Current { get; set; }

        /// <summary>0..1.</summary>
        public float Charge { get; set; }

        public float PowerKw => Voltage * Current / 1000f;
    }

    public class PantographState
    {
        /// <summary>Simulation component ID, e.g. "pantograph2".</summary>
        public string Name { get; set; }

        /// <summary>0 down, 1 fully up.</summary>
        public float Raised { get; set; }

        /// <summary>The head touches a wire, energised or not.</summary>
        public bool InContact { get; set; }

        public float WireVoltage { get; set; }
    }
}
