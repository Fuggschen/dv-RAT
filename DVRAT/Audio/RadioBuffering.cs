using System;

namespace DVRAT
{
    /// <summary>
    /// Simulates buffering stalls of a weak signal on interleaved samples. From two bars up the stream keeps pace.
    /// Below that the buffer runs dry and playback waits for <see cref="ResumeSeconds"/>. A stall silences samples
    /// instead of holding them, so playback stays live and the song title matches.
    /// </summary>
    public class RadioBuffering
    {
        public const double MaxBufferSeconds = 4;
        /// <summary>Buffer needed to resume after running dry.</summary>
        public const double ResumeSeconds = 2;

        // Stream seconds arriving per second: OneBarRate at one bar, 1 at two bars, FullRate at full signal.
        private const float OneBarRate = 0.5f;
        private const float FullRate = 2f;
        // Avoids a click when a stall starts.
        private const float FadeSeconds = 0.01f;

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly float _fadeStep;
        private double _buffered;
        private float _gain;
        private volatile bool _stalled;

        public RadioBuffering(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            _fadeStep = 1f / (FadeSeconds * sampleRate);
        }

        /// <summary>Readable from any thread.</summary>
        public bool Buffering => _stalled;

        /// <summary>Stream seconds arriving per played second. Strength is 0 to 1.</summary>
        public static float Rate(float strength)
        {
            if (strength < SignalLoad.OneBar) return 0f;
            if (strength < SignalLoad.TwoBars)
                return OneBarRate + (1f - OneBarRate) * (strength - SignalLoad.OneBar) / (SignalLoad.TwoBars - SignalLoad.OneBar);
            float t = Math.Min(1f, (strength - SignalLoad.TwoBars) / (1f - SignalLoad.TwoBars));
            return 1f + (FullRate - 1f) * t;
        }

        public void Process(float[] samples, int offset, int count, float strength)
        {
            float rate = Rate(strength);
            double frameSeconds = 1.0 / _sampleRate;
            for (int frame = offset; frame + _channels <= offset + count; frame += _channels)
            {
                // Starts empty, so a station tuned in at one bar fills first.
                _buffered = Math.Max(0, Math.Min(MaxBufferSeconds, _buffered + (rate - (_stalled ? 0 : 1)) * frameSeconds));
                if (!_stalled && _buffered <= 0 && rate < 1f)
                    _stalled = true;
                else if (_stalled && _buffered >= ResumeSeconds)
                    _stalled = false;

                _gain = _stalled ? Math.Max(0f, _gain - _fadeStep) : Math.Min(1f, _gain + _fadeStep);
                if (_gain >= 1f) continue;
                for (int c = 0; c < _channels; c++)
                    samples[frame + c] *= _gain;
            }
        }
    }
}
