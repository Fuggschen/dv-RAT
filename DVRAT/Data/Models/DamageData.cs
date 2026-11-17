using System.Collections.Generic;

namespace DVRAT
{
    public class DamageInfo
    {
        /// <summary>A loco without a powertrain leaves it out.</summary>
        public List<DamagePart> Parts { get; set; } = new List<DamagePart>();

        public List<FuseState> Fuses { get; set; } = new List<FuseState>();

        /// <summary>Null for a loco without a traction motor set.</summary>
        public int? WorkingMotors { get; set; }
        public int? TotalMotors { get; set; }
    }

    public class DamagePart
    {
        public string Name { get; set; }

        /// <summary>0..1, 1 undamaged.</summary>
        public float Health { get; set; }
    }

    public class FuseState
    {
        /// <summary>For example "electronics.ELECTRONICS_MAIN_FUSE".</summary>
        public string Id { get; set; }
        public bool On { get; set; }
    }
}
