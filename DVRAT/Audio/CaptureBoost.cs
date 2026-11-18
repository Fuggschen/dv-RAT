using System;

namespace DVRAT
{
    /// <summary>Boost for captured sound, set in <see cref="RATSettings.PcAudioBoostDb"/>. It comes after the program's own volume.</summary>
    public static class CaptureBoost
    {
        public const int MinDb = 0;
        public const int MaxDb = 24;
        public const int DefaultDb = 0;

        public static float Gain(int db) => (float)Math.Pow(10, Math.Max(MinDb, Math.Min(MaxDb, db)) / 20.0);

        /// <summary>Multiplies by <paramref name="gain"/> and rounds off what would go past full scale.</summary>
        public static void Apply(float[] samples, int count, float gain)
        {
            if (gain == 1f) return;
            for (int i = 0; i < count; i++)
                samples[i] = SoftKnee.Clip(samples[i] * gain);
        }
    }
}
