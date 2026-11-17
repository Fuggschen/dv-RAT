using System;

namespace DVRAT
{
    /// <summary>Gain by distance: full up to near, then 1/distance, faded to silence at far.</summary>
    public static class Falloff
    {
        /// <summary>Share of the range after which the fade to silence begins.</summary>
        public const float FadeStart = 0.5f;

        public static float Gain(float distance, float near, float far)
        {
            if (distance <= near) return 1f;
            if (distance >= far) return 0f;
            float speaker = near / distance;
            float t = Clamp01((distance - far * FadeStart) / (far * (1 - FadeStart)));
            float fade = 1f - t * t * (3f - 2f * t);
            return speaker * fade;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
