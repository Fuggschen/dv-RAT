using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>
    /// What an electric loco draws from the catenary wire, read from its ports. No Unity types, so it is testable.
    /// <list type="bullet">
    /// <item>Catenary for CCL locos (Knsgf/CCL-OCS), e.g. the WE-4U. Component IDs vary by author, so ports are found by their fixed names.</item>
    /// <item>The 2WE3-981, with its own ports. Its units are separate cars and unit A simulates both.</item>
    /// </list>
    /// </summary>
    public static class ElectricLoco
    {
        public const string WE3UnitA = "WE6981A";
        public const string WE3UnitB = "WE6981B";

        public const string SourceCatenaryForCcl = "Catenary for CCL";
        public const string SourceWE3 = "2WE3";

        /// <summary>Electric data of the loco, or of the nearest 2WE3 unit A for a 2WE3 unit. Null for any other loco.</summary>
        public static ElectricInfo Read(LocoInfo shown, IList<CarInfo> cars)
        {
            if (shown == null) return null;
            if (IsWE3(shown.CarType))
            {
                var unitA = NearestUnitA(shown, cars);
                return unitA == null ? null : ReadWE3(unitA);
            }
            return ReadCatenaryForCcl(shown);
        }

        public static bool IsWE3(string carType) =>
            carType != null && (carType.StartsWith(WE3UnitA, StringComparison.Ordinal)
                || carType.StartsWith(WE3UnitB, StringComparison.Ordinal));

        private static LocoInfo NearestUnitA(LocoInfo shown, IList<CarInfo> cars)
        {
            if (shown.CarType.StartsWith(WE3UnitA, StringComparison.Ordinal)) return shown;
            int at = cars?.IndexOf(shown) ?? -1;
            if (at < 0) return null;
            return cars.Select((car, index) => (car, index))
                .Where(c => c.car is LocoInfo && c.car.CarType != null && c.car.CarType.StartsWith(WE3UnitA, StringComparison.Ordinal))
                .OrderBy(c => Math.Abs(c.index - at))
                .Select(c => (LocoInfo)c.car)
                .FirstOrDefault();
        }

        private static ElectricInfo ReadCatenaryForCcl(LocoInfo loco)
        {
            var ports = Values(loco);
            // The bus bar collects the pantographs. A loco has one.
            string busBar = Owners(ports, "PANTOGRAPHS_RAISED_COUNT").FirstOrDefault(c =>
                ports.ContainsKey(c + ".PANTOGRAPH_INPUT_CURRENT") && ports.ContainsKey(c + ".SUPPLY_VOLTAGE"));
            if (busBar == null) return null;

            float voltage = ports[busBar + ".SUPPLY_VOLTAGE"];
            ports.TryGetValue(busBar + ".SUPPLY_VOLTAGE_NORMALIZED", out float normalized);
            // The draw is split evenly over the raised pantographs.
            float raised = Math.Max(1f, (float)Math.Round(ports[busBar + ".PANTOGRAPHS_RAISED_COUNT"]));
            var info = new ElectricInfo
            {
                CarId = loco.CarId,
                Source = SourceCatenaryForCcl,
                LineVoltage = voltage,
                // Nominal is supply divided by the normalized value, which fails at zero voltage.
                NominalVoltage = normalized > 0.05f ? voltage / normalized : (float?)null,
                LineCurrent = ports[busBar + ".PANTOGRAPH_INPUT_CURRENT"] * raised,
                PantographsReported = true,
                MotorTemperature = loco.TmTemperature,
            };
            // A second meter on the same car does not count, so take the first.
            string meter = Owners(ports, "ENERGY_CONSUMED").OrderBy(c => c, StringComparer.Ordinal).FirstOrDefault();
            if (meter != null)
                info.EnergyUnits = ports[meter + ".ENERGY_CONSUMED"];
            string motors = Owners(ports, "WORKING_TRACTION_MOTORS").FirstOrDefault();
            if (motors != null)
                info.WorkingMotors = (int)Math.Round(ports[motors + ".WORKING_TRACTION_MOTORS"]);

            foreach (string pantograph in Owners(ports, "PANTOGRAPH_RAISE_NORMALIZED").OrderBy(c => c, StringComparer.Ordinal))
            {
                ports.TryGetValue(pantograph + ".PANTOGRAPH_IN_CONTACT", out float contact);
                ports.TryGetValue(pantograph + ".WIRE_VOLTAGE", out float wire);
                info.Pantographs.Add(new PantographState
                {
                    Name = pantograph,
                    Raised = ports[pantograph + ".PANTOGRAPH_RAISE_NORMALIZED"],
                    InContact = contact > 0.5f,
                    WireVoltage = wire,
                });
            }
            return info;
        }

        /// <summary>Reads the 2WE3's own gauges. No port gives pantograph state, so <see cref="WE3Units"/> reads it. The motor count port covers only unit A's three motors, so it is left out.</summary>
        private static ElectricInfo ReadWE3(LocoInfo unitA)
        {
            var ports = Values(unitA);
            if (!ports.TryGetValue("[CustomGauges].SUPPLY", out float voltage)
                || !ports.TryGetValue("[CustomGauges].CURRENT_DRAW", out float current))
                return null;
            return new ElectricInfo
            {
                CarId = unitA.CarId,
                Source = SourceWE3,
                LineVoltage = voltage,
                LineCurrent = current,
                PantographsReported = false,
                MotorTemperature = unitA.TmTemperature,
                // The meter keeps its count in this port, so it is saved.
                EnergyUnits = Find(ports, "[LeftoverMeter].EXT_IN"),
            };
        }

        private static Dictionary<string, float> Values(LocoInfo loco)
        {
            var values = new Dictionary<string, float>();
            foreach (var port in loco.Ports)
                if (port.Id != null) values[port.Id] = port.Value;
            return values;
        }

        /// <summary>Components with this port, e.g. "busBar" for "busBar.PANTOGRAPHS_RAISED_COUNT".</summary>
        private static IEnumerable<string> Owners(Dictionary<string, float> ports, string portName) =>
            ports.Keys.Where(id => id.EndsWith("." + portName, StringComparison.Ordinal))
                .Select(id => id.Substring(0, id.Length - portName.Length - 1));

        private static float? Find(Dictionary<string, float> ports, string id) =>
            ports.TryGetValue(id, out float value) ? value : (float?)null;
    }
}
