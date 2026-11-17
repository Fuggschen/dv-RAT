using System;

namespace DVRAT
{
    /// <summary>How long remote data takes to arrive at a given radio signal strength.</summary>
    public static class SignalLoad
    {
        /// <summary>Signal strength needed for one, two and three bars.</summary>
        public const float OneBar = 0.25f;
        public const float TwoBars = 0.5f;
        public const float ThreeBars = 0.75f;

        // Seconds at each threshold and at full signal. Values in between blend linearly.
        private static readonly float[] Strengths = { OneBar, TwoBars, ThreeBars, 1f };
        private static readonly float[] Seconds = { 10f, 5f, 3f, 1f };

        /// <summary>Seconds a load takes at constant signal strength (0 to 1). Infinite below one bar.</summary>
        /// <param name="baseSeconds">Extra time on top of the signal's.</param>
        public static float Duration(float strength, float baseSeconds = 0f)
        {
            if (strength < OneBar) return float.PositiveInfinity;
            strength = Math.Min(1f, strength);

            int upper = 1;
            while (upper < Strengths.Length - 1 && strength > Strengths[upper])
                upper++;
            float t = (strength - Strengths[upper - 1]) / (Strengths[upper] - Strengths[upper - 1]);
            float signal = Seconds[upper - 1] + (Seconds[upper] - Seconds[upper - 1]) * t;
            return Math.Max(0f, baseSeconds) + signal;
        }

        /// <summary>Progress (0 to 1) after <paramref name="deltaTime"/> more seconds. Follows the live signal, stalling below one bar.</summary>
        public static float Advance(float progress, float deltaTime, float strength, float baseSeconds = 0f)
        {
            float duration = Duration(strength, baseSeconds);
            if (float.IsInfinity(duration)) return progress;
            return Math.Min(1f, progress + deltaTime / duration);
        }
    }
}
