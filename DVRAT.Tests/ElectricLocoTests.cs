using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

namespace DVRAT.Tests
{
    public class ElectricLocoTests
    {
        private static LocoInfo Loco(string id, string type, params (string id, float value)[] ports)
        {
            var loco = new LocoInfo { CarId = id, CarType = type };
            loco.Ports.AddRange(ports.Select(p => new SimPort { Id = p.id, Type = "GENERIC", Value = p.value }));
            return loco;
        }

        // Port names as the WE-4U names its Catenary for CCL components.
        private static LocoInfo WE4U(float raised2, float contact2)
        {
            var loco = Loco("L-1", "WE4U",
                ("busBar.SUPPLY_VOLTAGE", 1480f), ("busBar.SUPPLY_VOLTAGE_NORMALIZED", 1480f / 1500f),
                ("busBar.PANTOGRAPH_INPUT_CURRENT", 450f), ("busBar.PANTOGRAPHS_RAISED_COUNT", 2f),
                ("pantograph.PANTOGRAPH_RAISE_NORMALIZED", 0f), ("pantograph.PANTOGRAPH_IN_CONTACT", 0f), ("pantograph.WIRE_VOLTAGE", 0f),
                ("pantograph2.PANTOGRAPH_RAISE_NORMALIZED", raised2), ("pantograph2.PANTOGRAPH_IN_CONTACT", contact2),
                ("pantograph2.WIRE_VOLTAGE", 1490f),
                ("tm.WORKING_TRACTION_MOTORS", 4f), ("electricityMeter.ENERGY_CONSUMED", 20f));
            loco.TmTemperature = 62f;
            return loco;
        }

        [Fact]
        public void CatenaryForCclLocoIsFoundByPortNames()
        {
            var loco = WE4U(1f, 1f);

            var electric = ElectricLoco.Read(loco, new List<CarInfo> { loco });

            Assert.Equal("Catenary for CCL", electric.Source);
            Assert.Equal(1480f, electric.LineVoltage);
            Assert.Equal(1500f, electric.NominalVoltage.Value, 1);
            Assert.Equal(900f, electric.LineCurrent);
            Assert.Equal(1332f, electric.PowerKw, 1);
            Assert.Equal(4, electric.WorkingMotors);
            Assert.Equal(62f, electric.MotorTemperature);
            Assert.Equal(new[] { "pantograph", "pantograph2" }, electric.Pantographs.Select(p => p.Name));
            Assert.True(electric.Pantographs[1].InContact);
            Assert.Equal(20f, electric.EnergyUnits);
            Assert.Null(electric.EnergyKwh);
        }

        [Fact]
        public void EnergyShowsKwh()
        {
            // The Catenary for CCL default: 2/3 units per kWh.
            var loco = WE4U(1f, 1f);
            var electric = ElectricLoco.Read(loco, new List<CarInfo> { loco });
            electric.KwhPerUnit = 1.5f;
            var train = new TrainData { Cars = { loco }, Locomotive = loco, Electric = electric };

            var bound = (Dictionary<string, object>)PageBindings.Build(train, null, null, null, new Dictionary<string, string>(), id => id)["electric"];

            Assert.Equal("30.0 kWh", bound["energy"]);
        }

        [Fact]
        public void UnknownFactorShowsNoKwh()
        {
            var loco = WE4U(1f, 1f);
            var train = new TrainData { Cars = { loco }, Locomotive = loco, Electric = ElectricLoco.Read(loco, new List<CarInfo> { loco }) };

            var bound = (Dictionary<string, object>)PageBindings.Build(train, null, null, null, new Dictionary<string, string>(), id => id)["electric"];

            Assert.Equal("--", bound["energy"]);
        }

        [Fact]
        public void FeeIsShownForChargedLocosOnly()
        {
            var charged = new LocoInfo { CarId = "L-1", Fee = 1234.5f };
            var sandbox = new LocoInfo { CarId = "L-2" };

            object Bind(LocoInfo loco, string key) =>
                ((Dictionary<string, object>)PageBindings.Build(new TrainData { Cars = { loco }, Locomotive = loco }, null, null, null,
                    new Dictionary<string, string>(), id => id)["loco"])[key];

            Assert.Equal("$1,235", Bind(charged, "fee"));
            Assert.True((bool)Bind(charged, "hasFee"));
            Assert.False((bool)Bind(sandbox, "hasFee"));
        }

        [Fact]
        public void NoVoltageGivesNoNominalVoltage()
        {
            var loco = Loco("L-1", "WE4U", ("busBar.SUPPLY_VOLTAGE", 0f), ("busBar.SUPPLY_VOLTAGE_NORMALIZED", 0f),
                ("busBar.PANTOGRAPH_INPUT_CURRENT", 0f), ("busBar.PANTOGRAPHS_RAISED_COUNT", 0f));

            Assert.Null(ElectricLoco.Read(loco, new List<CarInfo> { loco }).NominalVoltage);
        }

        [Fact]
        public void RaisedPantographWithoutContactIsOffWire()
        {
            var loco = WE4U(1f, 0f);
            var train = new TrainData { Cars = { loco }, Locomotive = loco, Electric = ElectricLoco.Read(loco, new List<CarInfo> { loco }) };

            var electric = (Dictionary<string, object>)PageBindings.Build(train, null, null, null, new Dictionary<string, string>(), id => id)["electric"];

            Assert.True((bool)electric["offWire"]);
            Assert.Equal(1, electric["raisedCount"]);
        }

        [Fact]
        public void DieselIsNotElectric()
        {
            var loco = Loco("L-1", "LocoDE2", ("engine.RPM", 800f), ("tmHeat.TEMPERATURE", 50f));

            Assert.Null(ElectricLoco.Read(loco, new List<CarInfo> { loco }));
        }

        [Fact]
        public void WE3UnitBShowsTheNearestUnitA()
        {
            var farA = Loco("A-1", "WE6981A", ("[CustomGauges].SUPPLY", 1000f), ("[CustomGauges].CURRENT_DRAW", 1f));
            var wagon = new CarInfo { CarId = "W-1", CarType = "FlatbedStakes" };
            var nearA = Loco("A-2", "WE6981A", ("[CustomGauges].SUPPLY", 1550f), ("[CustomGauges].CURRENT_DRAW", -300f),
                ("[LeftoverMeter].EXT_IN", 7f));
            nearA.TmTemperature = 80f;
            var unitB = Loco("B-2", "WE6981B");

            var electric = ElectricLoco.Read(unitB, new List<CarInfo> { farA, wagon, nearA, unitB });

            Assert.Equal("2WE3", electric.Source);
            Assert.Equal("A-2", electric.CarId);
            Assert.Equal(1550f, electric.LineVoltage);
            Assert.Equal(-300f, electric.LineCurrent);
            Assert.False(electric.PantographsReported);
            Assert.Null(electric.WorkingMotors);
            Assert.Equal(7f, electric.EnergyUnits);
        }

        [Fact]
        public void WE3UnitBAloneIsNotShown()
        {
            var unitB = Loco("B-1", "WE6981B");

            Assert.Null(ElectricLoco.Read(unitB, new List<CarInfo> { unitB }));
        }
    }
}
