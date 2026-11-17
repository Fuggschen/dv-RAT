using System;

namespace DVRAT
{
    /// <summary>Boost for captured sound, which plays far quieter than a local song.</summary>
    public static class CaptureBoost
    {
        public const int Db = 18;
        /// <summary>The Linux helper already sets the stream to full volume.</summary>
        public const int LinuxDb = 6;

        public static float Gain(int db) => (float)Math.Pow(10, db / 20.0);

        /// <summary>Multiplies by <paramref name="gain"/> and rounds off what would go past full scale.</summary>
        public static void Apply(float[] samples, int count, float gain)
        {
            if (gain == 1f) return;
            for (int i = 0; i < count; i++)
                samples[i] = SoftKnee.Clip(samples[i] * gain);
        }
    }
}
