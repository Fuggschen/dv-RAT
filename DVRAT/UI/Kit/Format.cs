using System;
using System.Globalization;

namespace DVRAT
{
    public static class Format
    {
        /// <summary>Distances at or above this mean "cannot stop" and show as unknown.</summary>
        private const float NoValue = 999999f;

        public static string Distance(float meters)
        {
            if (meters >= NoValue || float.IsNaN(meters)) return "--";
            return meters < 1000f ? $"{meters:F0} m" : $"{meters / 1000f:F1} km";
        }

        public static string Duration(float seconds)
        {
            if (seconds >= 3600f) return $"{seconds / 3600f:F1} h";
            if (seconds >= 60f) return $"{seconds / 60f:F0} min";
            return $"{seconds:F0} s";
        }

        /// <summary>"3:07", or "1:02:05" from an hour on.</summary>
        public static string PlayTime(float seconds)
        {
            int total = (int)Math.Max(0f, seconds);
            int hours = total / 3600, minutes = total / 60 % 60, rest = total % 60;
            return hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes}:{rest:00}";
        }

        public static string Duration(TimeSpan time) =>
            time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes} min" : $"{(int)time.TotalMinutes} min {time.Seconds} s";

        /// <summary>"1 car", "3 cars". The plural is the noun plus "s".</summary>
        public static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        /// <summary>"12.5 W", or "1.2 kW" from 1000 W on.</summary>
        public static string Power(float watts) =>
            Math.Abs(watts) < 1000f
                ? watts.ToString("F1", CultureInfo.InvariantCulture) + " W"
                : (watts / 1000f).ToString("F1", CultureInfo.InvariantCulture) + " kW";

        /// <summary>"840 Wh", or "28.8 kWh" from 1000 Wh on.</summary>
        public static string Energy(float wattHours) =>
            Math.Abs(wattHours) < 1000f
                ? wattHours.ToString("F0", CultureInfo.InvariantCulture) + " Wh"
                : (wattHours / 1000f).ToString("F1", CultureInfo.InvariantCulture) + " kWh";

        public static string Percent(float fraction) => $"{fraction * 100f:F0} %";

        public static string Grade(float percent) => $"{percent:+0.0;-0.0;0.0} %";

        /// <summary>Rounded first, so -0.3 shows as "0 °C", not "-0 °C".</summary>
        public static string Temperature(float celsius) => $"{(int)Math.Round(celsius, MidpointRounding.AwayFromZero)} °C";
    }
}
