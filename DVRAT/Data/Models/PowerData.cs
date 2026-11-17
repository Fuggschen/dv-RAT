using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Power network of a car from the Solar Gadgets mod. W, Wh of game time, charge in percent.</summary>
    public class CarPower
    {
        /// <summary>Null for the train's total.</summary>
        public string CarId { get; set; }

        /// <summary>Solar panels plus the alternator of a running engine.</summary>
        public float Supply { get; set; }

        public float Demand { get; set; }

        /// <summary>Capacity-weighted, null without a battery.</summary>
        public float? Charge { get; set; }

        public float Capacity { get; set; }

        /// <summary>Supply minus demand into (+) or out of (-) the batteries, 0 when none can take part.</summary>
        public float Net { get; set; }

        /// <summary>Energy the participating batteries can still give.</summary>
        public float Stored { get; set; }

        /// <summary>Energy the participating batteries can still take.</summary>
        public float Room { get; set; }

        public List<PowerBattery> Batteries { get; set; } = new List<PowerBattery>();

        public List<PowerConsumer> Consumers { get; set; } = new List<PowerConsumer>();

        /// <summary>Change of <see cref="Charge"/> per game hour.</summary>
        public float RatePctPerHour => Capacity > 0f ? Net / Capacity * 100f : 0f;

        /// <summary>Null while steady.</summary>
        public float? HoursLeft
        {
            get
            {
                if (Math.Abs(Net) < 0.001f) return null;
                return (Net > 0f ? Room : Stored) / Math.Abs(Net);
            }
        }
    }

    public class PowerBattery
    {
        /// <summary>"Loco battery" for the loco's own, else the gadget's name.</summary>
        public string Name { get; set; }

        /// <summary>An unsoldered gadget is not part of the network.</summary>
        public bool Soldered { get; set; }

        public float Charge { get; set; }
        public float Capacity { get; set; }
    }

    public class PowerConsumer
    {
        public string Name { get; set; }
        public float Draw { get; set; }
    }
}
