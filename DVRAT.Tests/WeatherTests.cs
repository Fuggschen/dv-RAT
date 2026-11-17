using System;
using System.Collections.Generic;
using DVRAT;
using Xunit;

namespace DVRAT.Tests
{
    public class WeatherTests
    {
        // The game forecasts at 06:00 for the next 24 hours, so the last slots are on the next day.
        private static List<ForecastSlot> Day()
        {
            var slots = new List<ForecastSlot>();
            var start = new DateTime(2026, 5, 1, 6, 0, 0);
            for (int i = 0; i < 8; i++)
                slots.Add(new ForecastSlot { Start = start.AddHours(i * 3), Hours = 3, Icon = "Clear_Day" });
            return slots;
        }

        [Theory]
        [InlineData(6, 0, 1, 0)]
        [InlineData(8, 59, 1, 0)]
        [InlineData(9, 0, 1, 1)]
        [InlineData(23, 30, 1, 5)]
        [InlineData(2, 0, 2, 6)]
        [InlineData(5, 59, 2, 7)]
        public void TheSlotOfTheCurrentHourIsFoundAcrossMidnight(int hour, int minute, int day, int expected)
        {
            Assert.Equal(expected, ForecastSlot.IndexAt(Day(), new DateTime(2026, 5, day, hour, minute, 0)));
        }

        [Fact]
        public void NoSlotOutsideTheForecastOrWithoutTime()
        {
            Assert.Equal(-1, ForecastSlot.IndexAt(Day(), new DateTime(2026, 5, 2, 6, 0, 0)));
            Assert.Equal(-1, ForecastSlot.IndexAt(Day(), null));
            Assert.Equal(-1, ForecastSlot.IndexAt(new List<ForecastSlot>(), new DateTime(2026, 5, 1, 12, 0, 0)));
        }

        [Theory]
        [InlineData(-0.3f, "0 °C")]
        [InlineData(2.5f, "3 °C")]
        [InlineData(-2.5f, "-3 °C")]
        [InlineData(14.4f, "14 °C")]
        public void TemperaturesAreWholeDegrees(float celsius, string expected)
        {
            Assert.Equal(expected, Format.Temperature(celsius));
        }

        [Fact]
        public void DayAndNightIconsReadTheSame()
        {
            Assert.Equal("Partly cloudy", ForecastSlot.Describe("PartlyCloudy_Night"));
            Assert.Equal("Light rain", ForecastSlot.Describe("LightRain_Day"));
            Assert.Equal("", ForecastSlot.Describe(null));
        }

        [Fact]
        public void BindingsMarkTheCurrentSlotAndThePastOnes()
        {
            var world = new WorldData { GameTime = new DateTime(2026, 5, 1, 10, 0, 0), Forecast = Day() };
            world.Forecast[1].Icon = "HeavyRain";
            var root = PageBindings.Build(null, null, null, world, new Dictionary<string, string>(), id => id);

            var weather = (Dictionary<string, object>)root["weather"];
            var slots = (List<object>)weather["forecast"];
            Assert.Equal("Heavy rain", weather["now"]);
            Assert.Equal("2026-05-01", weather["date"]);
            Assert.True((bool)((Dictionary<string, object>)slots[0])["past"]);
            Assert.True((bool)((Dictionary<string, object>)slots[1])["now"]);
            Assert.False((bool)((Dictionary<string, object>)slots[2])["past"]);
            Assert.False((bool)((Dictionary<string, object>)root["seasons"])["installed"]);
        }

        [Fact]
        public void WeatherPageRowsShowForTextBindings()
        {
            var world = new WorldData
            {
                GameTime = new DateTime(2026, 5, 1, 10, 0, 0),
                Forecast = Day(),
                SeasonsInstalled = true,
                Seasons = new SeasonsData { Season = "Winter", NextSeason = "Spring", Blizzard = "No blizzard forecast" },
            };
            var root = PageBindings.Build(null, null, null, world, new Dictionary<string, string>(), id => id);

            // The page shows these rows on a non-empty text.
            Assert.True(Template.Evaluate("weather.now", root));
            Assert.True(Template.Evaluate("seasons.blizzard", root));
            Assert.False(Template.Evaluate("seasons.changing", root));
        }
    }
}
