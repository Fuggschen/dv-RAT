using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using LocoSim.Definitions;

namespace DVRAT
{
    /// <summary>
    /// Reads electric locos of Catenary for CCL and the 2WE3, and converts their meters' charge units to kWh:
    /// <list type="bullet">
    /// <item>CCL: author's consumption factor (units per kWh) times the difficulty's resource consumption.</item>
    /// <item>2WE3: the catenary mod's price per kWh divided by the unit price of 15 it assumes.</item>
    /// </list>
    /// </summary>
    public static class CatenaryMods
    {
        private static readonly ModWatch CclMod = new ModWatch("CatenaryForCCL");
        // The 2WE3 is a CCL car without an assembly. Its electrics run in the catenary mod.
        private static readonly ModWatch WE3Mod = new ModWatch("Catenary-DC");
        private const string MeterDefinitionType = "catenary_for_CCL.importer.electricity_meter_definition";
        private const string MeterFactorField = "electric_charge_consumption_factor";
        private const string WE3SettingsType = "electric_sim.catenary_editor.editor_settings";
        private const string WE3PriceProperty = "kWh_price";
        private const float WE3AssumedUnitPrice = 15f;

        private static bool _cclSearched, _cclFailed;
        private static FieldInfo _cclFactor;
        private static bool _we3Searched, _we3Failed;
        private static PropertyInfo _we3KwhPrice;

        public static IntegrationState CclState => State(FindCcl(), _cclFailed);

        public static IntegrationState WE3State => State(FindWE3(), _we3Failed);

        public static ElectricInfo Read(LocoInfo shown, IList<CarInfo> cars, IList<TrainCar> trainCars)
        {
            if (shown == null) return null;
            bool we3 = ElectricLoco.IsWE3(shown.CarType);
            if (we3 ? !FindWE3() : !FindCcl()) return null;
            try
            {
                var electric = ElectricLoco.Read(shown, cars);
                if (electric != null && we3)
                {
                    var unitA = trainCars.FirstOrDefault(c => c != null && c.ID == electric.CarId);
                    electric.Pantographs = WE3Units.Read(unitA, trainCars);
                    electric.PantographsReported = true;
                    // Outside a regenerative mode a negative draw is circuit rounding, not power fed back.
                    if (WE3Units.Regenerative(unitA) == false && electric.LineCurrent < 0f)
                        electric.LineCurrent = 0f;
                }
                var car = electric == null ? null : trainCars.FirstOrDefault(c => c != null && c.ID == electric.CarId);
                if (electric?.EnergyUnits != null)
                    electric.KwhPerUnit = we3 ? WE3KwhPerUnit() : CclKwhPerUnit(car);
                if (electric != null && !we3)
                    electric.Battery = WE4UBattery.Read(car);
                return electric;
            }
            catch (Exception ex)
            {
                Fail(we3, $"reading {shown.CarId} failed: {(ex.InnerException ?? ex).Message}");
                return null;
            }
        }

        private static IntegrationState State(bool found, bool failed) =>
            found ? IntegrationState.Active : failed ? IntegrationState.Failed : IntegrationState.Inactive;

        private static float? CclKwhPerUnit(TrainCar car)
        {
            var meter = car?.GetComponentsInChildren<SimComponentDefinition>(true)
                .FirstOrDefault(d => d.GetType().FullName == MeterDefinitionType);
            if (meter == null) return null;
            float unitsPerKwh = (float)_cclFactor.GetValue(meter) * Globals.G.GameParams.ResourceConsumptionModifier;
            return unitsPerKwh > 0f ? 1f / unitsPerKwh : (float?)null;
        }

        private static float? WE3KwhPerUnit()
        {
            float kwhPrice = (float)_we3KwhPrice.GetValue(null);
            return kwhPrice > 0f ? WE3AssumedUnitPrice / kwhPrice : (float?)null;
        }

        private static bool FindCcl()
        {
            if (_cclFailed || !CclMod.Active) return false;
            if (!_cclSearched)
            {
                _cclSearched = true;
                var meter = CclMod.FindType(MeterDefinitionType);
                if (meter == null)
                {
                    Fail(false, $"{MeterDefinitionType} was not found");
                    return false;
                }
                _cclFactor = meter.GetField(MeterFactorField, BindingFlags.Public | BindingFlags.Instance);
                if (_cclFactor == null)
                {
                    Fail(false, $"{MeterFactorField} of its electricity meter was not found");
                    return false;
                }
                RATMain.Log("Catenary for CCL found, its electric locos are shown on the Electric page");
            }
            return _cclFactor != null;
        }

        private static bool FindWE3()
        {
            if (_we3Failed || !WE3Mod.Active) return false;
            if (!_we3Searched)
            {
                _we3Searched = true;
                var settings = WE3Mod.FindType(WE3SettingsType);
                if (settings == null)
                {
                    Fail(true, $"{WE3SettingsType} was not found");
                    return false;
                }
                _we3KwhPrice = settings.GetProperty(WE3PriceProperty, BindingFlags.Public | BindingFlags.Static);
                if (_we3KwhPrice == null)
                {
                    Fail(true, $"its {WE3PriceProperty} setting was not found");
                    return false;
                }
                string missing = WE3Units.Find(settings.Assembly);
                if (missing != null)
                {
                    _we3KwhPrice = null;
                    Fail(true, $"{missing} was not found");
                    return false;
                }
                RATMain.Log("1500 V DC Catenary found, its 2WE3 is shown on the Electric page");
            }
            return _we3KwhPrice != null;
        }

        private static void Fail(bool we3, string reason)
        {
            if (we3) _we3Failed = true;
            else _cclFailed = true;
            RATMain.LogWarning($"{(we3 ? "1500 V DC Catenary" : "Catenary for CCL")}: {reason}. Its electric locos are left out.");
        }
    }
}
