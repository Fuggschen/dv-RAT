using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DVRAT
{
    /// <summary>2WE3 state without a port: each unit's pantograph and sidepan, and unit A's selector. Read from private simulation fields by reflection. See <see cref="CatenaryMods"/> for missing members.</summary>
    internal static class WE3Units
    {
        private const string SpawnHandlerType = "electric_sim.car_spawn_handler";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>A head this far above its rest position is up, in m.</summary>
        private const float LiftedHead = 0.05f;

        private static FieldInfo _unitsA, _unitsB;
        /// <summary>Selector modes series_regenerative and parallel_regenerative. Only these feed the line.</summary>
        private const int SeriesRegenerative = 0, ParallelRegenerative = 1;

        private static FieldInfo _pantographA, _pantographB, _roofBusA, _roofBusB, _selector;
        private static FieldInfo _headHeight, _restHeight, _sidepanPivot, _sidepanArm;
        private static PropertyInfo _pantographVoltage, _sidepanVoltage;

        /// <summary>Returns the name of the first missing member, or null when all are there.</summary>
        public static string Find(Assembly mod)
        {
            var handler = mod.GetType(SpawnHandlerType, false);
            _unitsA = handler?.GetField("_all_A_units", BindingFlags.NonPublic | BindingFlags.Static);
            _unitsB = handler?.GetField("_all_B_units", BindingFlags.NonPublic | BindingFlags.Static);
            if (_unitsA == null || _unitsB == null) return SpawnHandlerType + " units";

            var unitA = _unitsA.FieldType.GetGenericArguments().ElementAtOrDefault(1);
            var unitB = _unitsB.FieldType.GetGenericArguments().ElementAtOrDefault(1);
            _pantographA = unitA?.GetField("_pantograph", Private);
            _pantographB = unitB?.GetField("_pantograph", Private);
            _roofBusA = unitA?.GetField("_roof_bus", Private);
            _roofBusB = unitB?.GetField("_roof_bus", Private);
            if (_pantographA == null || _pantographB == null || _roofBusA == null || _roofBusB == null)
                return "the pantograph of a unit";
            _selector = unitA.GetField("_selector", Private);
            if (_selector?.FieldType != typeof(int)) return "the selector of unit A";

            var pantograph = _pantographA.FieldType;
            _headHeight = pantograph.GetField("_current_height", Private);
            _restHeight = pantograph.GetField("_initial_head_height", Private);
            _sidepanPivot = pantograph.GetField("_side_pivot_relative_position", Private);
            _sidepanArm = pantograph.GetField("_side_arm_relative_position", Private);
            var roofBus = _roofBusA.FieldType;
            _pantographVoltage = roofBus.GetProperty("pantograph_voltage", Public);
            _sidepanVoltage = roofBus.GetProperty("sidepan_voltage", Public);
            if (new MemberInfo[] { _headHeight, _restHeight, _sidepanPivot, _sidepanArm, _pantographVoltage, _sidepanVoltage }
                .Any(m => m == null))
                return "the position or voltage of a pantograph";
            return null;
        }

        /// <summary>Pantographs of unit A and the nearest unit B. Contact means a live wire, as the mod knows no other.</summary>
        public static List<PantographState> Read(TrainCar unitA, IList<TrainCar> cars)
        {
            var states = new List<PantographState>();
            if (unitA == null) return states;
            var unitsA = (IDictionary)_unitsA.GetValue(null);
            var unitsB = (IDictionary)_unitsB.GetValue(null);

            if (unitsA.Contains(unitA))
                Add(states, "Unit A", unitsA[unitA], _pantographA, _roofBusA);
            int at = cars.IndexOf(unitA);
            var unitB = cars.Where(c => c != null && unitsB.Contains(c))
                .OrderBy(c => Math.Abs(cars.IndexOf(c) - at)).FirstOrDefault();
            if (unitB != null)
                Add(states, "Unit B", unitsB[unitB], _pantographB, _roofBusB);
            return states;
        }

        /// <summary>Null for a car that is not unit A.</summary>
        public static bool? Regenerative(TrainCar unitA)
        {
            var unitsA = (IDictionary)_unitsA.GetValue(null);
            if (unitA == null || !unitsA.Contains(unitA)) return null;
            int selector = (int)_selector.GetValue(unitsA[unitA]);
            return selector == SeriesRegenerative || selector == ParallelRegenerative;
        }

        private static void Add(List<PantographState> states, string unit, object simulation, FieldInfo pantographField, FieldInfo roofBusField)
        {
            object pantograph = pantographField.GetValue(simulation);
            object roofBus = roofBusField.GetValue(simulation);
            float lifted = (float)_headHeight.GetValue(pantograph) - (float)_restHeight.GetValue(pantograph);
            float roofVoltage = (float)_pantographVoltage.GetValue(roofBus);
            states.Add(new PantographState
            {
                Name = unit + " pantograph",
                Raised = lifted > LiftedHead ? 1f : 0f,
                InContact = roofVoltage > 0f,
                WireVoltage = roofVoltage,
            });

            float sidepan = Math.Min((float)_sidepanPivot.GetValue(pantograph), (float)_sidepanArm.GetValue(pantograph));
            float sideVoltage = (float)_sidepanVoltage.GetValue(roofBus);
            states.Add(new PantographState
            {
                Name = unit + " sidepan",
                Raised = sidepan,
                InContact = sideVoltage > 0f,
                WireVoltage = sideVoltage,
            });
        }
    }
}
