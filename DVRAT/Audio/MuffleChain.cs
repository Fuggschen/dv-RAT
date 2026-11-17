using System;

namespace DVRAT
{
    /// <summary>Low-pass plus gain for sound heard through a closed cab wall, faded over <see cref="FadeSeconds"/>.</summary>
    public class MuffleChain
    {
        public const double CutoffHz = 1200;
        public const float GainDb = -6f;
        /// <summary>Gain for a bass-only source, which a wall passes with less loss.</summary>
        public const float BassGainDb = -3f;
        public const float FadeSeconds = 0.2f;

        private readonly Biquad[] _lowPass;
        private readonly float _step;
        private readonly float _gain;
        private float _mix;

        public int Channels { get; }

        /// <param name="lowPass">False for a bass-only source: it only gets quieter, by <see cref="BassGainDb"/>.</param>
        public MuffleChain(int sampleRate, int channels, bool lowPass)
        {
            Channels = channels;
            _step = 1f / (sampleRate * FadeSeconds);
            _gain = (float)Math.Pow(10, (lowPass ? GainDb : BassGainDb) / 20);
            if (lowPass)
            {
                _lowPass = new Biquad[channels];
                for (int i = 0; i < channels; i++)
                    _lowPass[i] = Biquad.LowPass(sampleRate, CutoffHz);
            }
        }

        /// <summary>From 0 (clear) to 1 (behind the wall).</summary>
        public float Mix => _mix;

        /// <param name="data">Interleaved samples, changed in place.</param>
        public void Process(float[] data, bool walled)
        {
            float target = walled ? 1f : 0f;
            // Skipping the low-pass here only affects the first ms of the next fade, where its share is near zero.
            if (_mix == 0f && target == 0f) return;
            for (int frame = 0; frame + Channels <= data.Length; frame += Channels)
            {
                if (_mix < target) _mix = Math.Min(target, _mix + _step);
                else if (_mix > target) _mix = Math.Max(target, _mix - _step);
                for (int channel = 0; channel < Channels; channel++)
                {
                    float dry = data[frame + channel];
                    float wet = (_lowPass != null ? _lowPass[channel].Process(dry) : dry) * _gain;
                    data[frame + channel] = dry + _mix * (wet - dry);
                }
            }
        }
    }
}
