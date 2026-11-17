using System;

namespace DVRAT
{
    /// <summary>Below <see cref="Knee"/> samples pass untouched. Above it they bend towards full scale and never past it.</summary>
    public static class SoftKnee
    {
        public const float Knee = 0.8f;

        public static float Clip(float x)
        {
            float size = Math.Abs(x);
            if (size <= Knee) return x;
            float bent = Knee + (1f - Knee) * (float)Math.Tanh((size - Knee) / (1f - Knee));
            return x < 0 ? -bent : bent;
        }
    }
}
