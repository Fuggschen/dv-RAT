using System.Collections.Generic;
using System.Linq;
using DV.Damage;
using DV.Simulation.Cars;
using LocoSim.Definitions;

namespace DVRAT
{
    internal static class LocoDamage
    {
        public static DamageInfo Read(TrainCar car, SimController sim)
        {
            var controller = car.GetComponent<DamageController>();
            if (controller == null) return null;

            var info = new DamageInfo();
            if (controller.bodyDamage != null)
                info.Parts.Add(new DamagePart { Name = "Body", Health = controller.bodyDamage.HealthPercentage });
            Add(info, "Wheels", controller.wheels);
            Add(info, "Mechanical powertrain", controller.mechanicalPT);
            Add(info, "Electrical powertrain", controller.electricalPT);

            var flow = sim?.simFlow;
            foreach (var fuse in flow?.AllFuses ?? new List<LocoSim.Implementations.Fuse>())
                if (fuse?.id != null)
                    info.Fuses.Add(new FuseState { Id = fuse.id, On = fuse.State });

            var motors = sim?.connectionsDefinition?.executionOrder?.OfType<TractionMotorSetDefinition>().FirstOrDefault();
            string working = motors?.ID + ".WORKING_TRACTION_MOTORS";
            var port = motors == null ? null : flow?.AllPorts?.FirstOrDefault(p => p?.id == working);
            if (port != null)
            {
                info.WorkingMotors = (int)System.Math.Round(port.Value);
                info.TotalMotors = motors.numberOfTractionMotors;
            }
            return info;
        }

        private static void Add(DamageInfo info, string name, TrainDamage damage)
        {
            if (damage != null && damage.fullHitPoints > 0)
                info.Parts.Add(new DamagePart { Name = name, Health = damage.HealthPercentage });
        }
    }
}
