using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Averages over a span of the daily forecast shown on the station posters.</summary>
    public class ForecastSlot
    {
        public DateTime Start { get; set; }
        public float Hours { get; set; }

        /// <summary>Name of WeatherForecastIconType, e.g. "LightRain_Day".</summary>
        public string Icon { get; set; }

        public float CloudPct { get; set; }
        public float RainPct { get; set; }
        public float FogPct { get; set; }
        public float ThunderPct { get; set; }

        /// <summary>From DVSeasons, blizzards left out. Null without it.</summary>
        public float? TemperatureC { get; set; }

        public DateTime End => Start.AddHours(Hours);

        public static string Describe(string icon)
        {
            switch (icon)
            {
                case "Thunder": return "Thunderstorm";
                case "Overcast": return "Overcast";
                case "PartlyCloudy_Day": case "PartlyCloudy_Night": return "Partly cloudy";
                case "Clear_Day": case "Clear_Night": return "Clear";
                case "LightRain_Day": case "LightRain_Night": return "Light rain";
                case "HeavyRain": return "Heavy rain";
                case "LightFog_Day": case "LightFog_Night": return "Light fog";
                case "HeavyFog": return "Heavy fog";
                default: return "";
            }
        }

        /// <summary>-1 when no slot contains <paramref name="time"/>.</summary>
        public static int IndexAt(IList<ForecastSlot> slots, DateTime? time)
        {
            if (slots == null || !time.HasValue) return -1;
            for (int i = 0; i < slots.Count; i++)
                if (time.Value >= slots[i].Start && time.Value < slots[i].End)
                    return i;
            return -1;
        }
    }

    /// <summary>Season and air temperature from the DVSeasons mod.</summary>
    public class SeasonsData
    {
        /// <summary>Spring, Summer, Autumn or Winter.</summary>
        public string Season { get; set; }

        public string NextSeason { get; set; }

        /// <summary>Progress towards <see cref="NextSeason"/>, 0 - 1.</summary>
        public float Transition { get; set; }

        /// <summary>0 - 1.</summary>
        public float Snow { get; set; }

        /// <summary>Includes a blizzard.</summary>
        public float TemperatureC { get; set; }

        /// <summary>DVSeasons' own line about the next or current blizzard.</summary>
        public string Blizzard { get; set; }
    }
}
