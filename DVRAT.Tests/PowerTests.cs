using DVRAT;
using Xunit;

namespace DVRAT.Tests
{
    public class PowerTests
    {
        // Solar Gadgets knows which batteries take part.
        private static CarPower Car(float supply, float demand, float flow, float? charge, float capacity, float stored, float room) => new CarPower
        {
            Supply = supply,
            Demand = demand,
            Net = flow,
            Charge = charge,
            Capacity = capacity,
            Stored = stored,
            Room = room,
        };

        [Fact]
        public void DrainingShowsTheRateAndTimeUntilEmpty()
        {
            // 2 W out of 100 Wh at 50 %: 2 %/h, 25 hours left.
            var car = Car(0f, 2f, -2f, 50f, 100f, 50f, 50f);

            Assert.Equal(-2f, car.RatePctPerHour, 3);
            Assert.Equal(25f, car.HoursLeft.Value, 3);
            Assert.Equal("-2.0 %/h, empty in 25 h 0 min", PageBindings.PowerRate(car));
        }

        [Fact]
        public void ChargingShowsTheTimeUntilFull()
        {
            var car = Car(5f, 1f, 4f, 80f, 100f, 80f, 20f);

            Assert.Equal("+4.0 %/h, full in 5 h 0 min", PageBindings.PowerRate(car));
        }

        [Fact]
        public void OnlyTheBatteriesThatTakePartCountForTheTimeLeft()
        {
            // An external battery at 10 % drains; the loco battery is full but its electrics are off.
            var car = Car(0f, 2f, -2f, 55f, 200f, 10f, 90f);

            Assert.Equal(5f, car.HoursLeft.Value, 3);
        }

        [Theory]
        [InlineData(3f, 1f, 100f, 0f, "Full")]
        [InlineData(3f, 1f, 60f, 0f, "Not charging")]
        [InlineData(0f, 1f, 0f, 0f, "Empty")]
        [InlineData(1f, 1f, 40f, 40f, "Steady")]
        public void NothingFlows(float supply, float demand, float charge, float stored, string text)
        {
            var car = Car(supply, demand, 0f, charge, 100f, stored, 0f);

            Assert.Null(car.HoursLeft);
            Assert.Equal(text, PageBindings.PowerRate(car));
        }

        [Theory]
        [InlineData(12.5f, "12.5 W")]
        [InlineData(150f, "150.0 W")]
        [InlineData(1200f, "1.2 kW")]
        public void PowerSwitchesToKilowatts(float watts, string text)
        {
            Assert.Equal(text, Format.Power(watts));
        }

        [Theory]
        [InlineData(840f, "840 Wh")]
        [InlineData(28800f, "28.8 kWh")]
        public void EnergySwitchesToKilowattHours(float wattHours, string text)
        {
            Assert.Equal(text, Format.Energy(wattHours));
        }

        [Fact]
        public void ACarWithoutBatteryHasNoRate()
        {
            Assert.Equal("No battery", PageBindings.PowerRate(Car(0f, 3f, 0f, null, 0f, 0f, 0f)));
        }
    }
}
