using System;
using System.Linq;
using DV.WeatherSystem;
using UnityEngine;

namespace DVRAT
{
    /// <summary>The game's forecast icons, from the station forecast posters. The mapping exists only while a poster is loaded, so it is searched for again every few seconds.</summary>
    public static class WeatherIcons
    {
        private const float SearchInterval = 5f;

        private static WeatherForecastIconMapping _mapping;
        private static float _nextSearch;

        /// <summary>Takes a WeatherForecastIconType name. Null for an unknown name or while no poster is loaded.</summary>
        public static Sprite For(string icon)
        {
            if (string.IsNullOrEmpty(icon) || !Enum.TryParse(icon, out WeatherForecastIconType type)
                || !Enum.IsDefined(typeof(WeatherForecastIconType), type))
                return null;
            // Unity null check, also true for an unloaded mapping.
            if (_mapping == null && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + SearchInterval;
                _mapping = Resources.FindObjectsOfTypeAll<WeatherForecastIconMapping>().FirstOrDefault();
            }
            if (_mapping == null) return null;
            var sprite = _mapping.GetIconFor(type);
            return sprite != null ? sprite : null;
        }
    }
}
