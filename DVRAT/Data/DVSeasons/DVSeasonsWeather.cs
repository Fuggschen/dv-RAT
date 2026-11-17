using System;
using System.Linq;
using System.Reflection;
using DV.Utils;
using DV.WeatherSystem;

namespace DVRAT
{
    /// <summary>Reads season and air temperature from DVSeasons, which has no API, via the private static <c>DVSeasons.Mod.Main.runtime</c>. A missing member switches the reader off with one log warning.</summary>
    public static class DVSeasonsWeather
    {
        private static readonly ModWatch Mod = new ModWatch("DVSeasons");
        private const string MainTypeName = "DVSeasons.Mod.Main";
        private const string ProfileTypeName = "DVSeasons.Core.AirTemperatureProfile";

        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static bool _searched;
        private static bool _failed;
        private static FieldInfo _runtime;
        private static PropertyInfo _ready, _state, _blizzard;
        private static FieldInfo _cycle;
        private static MethodInfo _baseState;
        private static PropertyInfo _temperature, _current, _next, _transition, _snow;
        private static MethodInfo _evaluate;

        public static IntegrationState State =>
            Find() ? IntegrationState.Active : _failed ? IntegrationState.Failed : IntegrationState.Inactive;

        /// <summary>Fills <see cref="WorldData.Seasons"/> and the forecast temperatures. Leaves them empty while DVSeasons has no temperature yet.</summary>
        public static void Read(WorldData data)
        {
            if (!Find()) return;
            data.SeasonsInstalled = true;
            try
            {
                object runtime = _runtime.GetValue(null);
                if (runtime == null || !(bool)_ready.GetValue(runtime)) return;
                object state = _state.GetValue(runtime);
                if (state == null) return;
                data.Seasons = new SeasonsData
                {
                    Season = _current.GetValue(state).ToString(),
                    NextSeason = _next.GetValue(state).ToString(),
                    Transition = (float)_transition.GetValue(state),
                    Snow = (float)_snow.GetValue(state),
                    TemperatureC = (float)_temperature.GetValue(state),
                    Blizzard = (string)_blizzard.GetValue(runtime),
                };
                EstimateForecast(data, runtime);
            }
            catch (Exception ex)
            {
                Fail($"reading the season failed: {(ex.InnerException ?? ex).Message}");
                data.Seasons = null;
            }
        }

        /// <summary>Temperature DVSeasons would compute for each slot's weather at its middle. Uses the season's mean, not the current air temperature.</summary>
        private static void EstimateForecast(WorldData data, object runtime)
        {
            if (data.Forecast.Count == 0) return;
            object cycle = _cycle.GetValue(runtime);
            object season = cycle == null ? null : _baseState.Invoke(cycle, null);
            if (season == null) return;
            // The forecast has no wind, so use the current one.
            var driver = SingletonBehaviour<WeatherDriver>.Instance;
            float wind = driver != null ? driver.WindSpeed.CurrentValue / 7f : 0f;
            foreach (var slot in data.Forecast)
            {
                var middle = slot.Start.AddHours(slot.Hours / 2f);
                double days = middle.Ticks / (double)TimeSpan.TicksPerDay;
                float hour = (float)middle.TimeOfDay.TotalHours;
                slot.TemperatureC = (float)_evaluate.Invoke(null, new object[]
                {
                    season, days, hour, slot.CloudPct / 100f, slot.RainPct / 100f, wind, slot.ThunderPct / 100f,
                });
            }
        }

        private static bool Find()
        {
            if (_failed || !Mod.Active) return false;
            if (_searched) return _runtime != null;
            _searched = true;

            var main = Mod.FindType(MainTypeName);
            // Lives in DVSeasons.Core.dll.
            var profile = Mod.FindType(ProfileTypeName);

            _runtime = main?.GetField("runtime", Static);
            var runtime = _runtime?.FieldType;
            _ready = runtime?.GetProperty("HasWeatherTemperature", Instance);
            _state = runtime?.GetProperty("CurrentState", Instance);
            _blizzard = runtime?.GetProperty("BlizzardStatus", Instance);
            _cycle = runtime?.GetField("cycle", Instance);
            _baseState = _cycle?.FieldType.GetMethod("GetState", Instance, null, Type.EmptyTypes, null);
            var state = _state?.PropertyType;
            _temperature = state?.GetProperty("TemperatureCelsius", Instance);
            _current = state?.GetProperty("Current", Instance);
            _next = state?.GetProperty("Next", Instance);
            _transition = state?.GetProperty("Transition", Instance);
            _snow = state?.GetProperty("SnowAmount", Instance);
            _evaluate = state == null ? null : profile?.GetMethod("Evaluate", Static, null,
                new[] { state, typeof(double), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) }, null);

            var members = new MemberInfo[]
            {
                _runtime, _ready, _state, _blizzard, _cycle, _baseState, _temperature, _current, _next, _transition, _snow, _evaluate,
            };
            if (members.Any(m => m == null))
            {
                Fail("its season state was not found");
                return false;
            }
            RATMain.Log("DVSeasons found, its season and temperature are shown on the Weather page");
            return true;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            _runtime = null;
            RATMain.LogWarning($"DVSeasons: {reason}. Its season and temperature are left out.");
        }
    }
}
