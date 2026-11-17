using System;

namespace DVRAT
{
    /// <summary>One channel's second-order filter (RBJ Audio EQ Cookbook). Transposed direct form II stays stable with float samples.</summary>
    public class Biquad
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public static Biquad LowPass(double sampleRate, double frequency, double q = 0.7071) =>
            new Biquad().SetLowPass(sampleRate, frequency, q);

        public static Biquad HighPass(double sampleRate, double frequency, double q = 0.7071) =>
            new Biquad().SetHighPass(sampleRate, frequency, q);

        public Biquad SetLowPass(double sampleRate, double frequency, double q)
        {
            Prepare(sampleRate, frequency, q, out double cos, out double alpha);
            return Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public Biquad SetHighPass(double sampleRate, double frequency, double q)
        {
            Prepare(sampleRate, frequency, q, out double cos, out double alpha);
            return Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public Biquad SetLowShelf(double sampleRate, double frequency, double gainDb)
        {
            double a = Math.Pow(10, gainDb / 40);
            Prepare(sampleRate, frequency, 0.7071, out double cos, out double alpha);
            double root = 2 * Math.Sqrt(a) * alpha;
            return Set(
                a * ((a + 1) - (a - 1) * cos + root),
                2 * a * ((a - 1) - (a + 1) * cos),
                a * ((a + 1) - (a - 1) * cos - root),
                (a + 1) + (a - 1) * cos + root,
                -2 * ((a - 1) + (a + 1) * cos),
                (a + 1) + (a - 1) * cos - root);
        }

        public Biquad SetHighShelf(double sampleRate, double frequency, double gainDb)
        {
            double a = Math.Pow(10, gainDb / 40);
            Prepare(sampleRate, frequency, 0.7071, out double cos, out double alpha);
            double root = 2 * Math.Sqrt(a) * alpha;
            return Set(
                a * ((a + 1) + (a - 1) * cos + root),
                -2 * a * ((a - 1) + (a + 1) * cos),
                a * ((a + 1) + (a - 1) * cos - root),
                (a + 1) - (a - 1) * cos + root,
                2 * ((a - 1) - (a + 1) * cos),
                (a + 1) - (a - 1) * cos - root);
        }

        public Biquad SetPeak(double sampleRate, double frequency, double q, double gainDb)
        {
            double a = Math.Pow(10, gainDb / 40);
            Prepare(sampleRate, frequency, q, out double cos, out double alpha);
            return Set(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
        }

        public float Process(float x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return (float)y;
        }

        private static void Prepare(double sampleRate, double frequency, double q, out double cos, out double alpha)
        {
            double w0 = 2 * Math.PI * Math.Min(frequency, sampleRate * 0.49) / sampleRate;
            cos = Math.Cos(w0);
            alpha = Math.Sin(w0) / (2 * q);
        }

        private Biquad Set(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
            return this;
        }
    }

    /// <summary>
    /// A loudspeaker's range, saturation and EQ. Values may change while sound runs. There is no mono setting:
    /// Unity runs filters after 3D panning, so mixing channels there would lose the sound's direction.
    /// </summary>
    public class ToneSettings
    {
        /// <summary>Range of the Lows/Mids/Highs keys, in dB.</summary>
        public const float MaxEqDb = 12f;

        /// <summary>0 for no limit.</summary>
        public float LowCutHz;
        /// <summary>0 for no limit.</summary>
        public float HighCutHz;
        /// <summary>1 is clean up to <see cref="SoftKnee.Knee"/>. Higher saturates sooner, like a small cheap speaker.</summary>
        public float Drive = 1f;
        public float LowsDb;
        public float MidsDb;
        public float HighsDb;

        public static ToneSettings TabletSpeaker() => new ToneSettings { LowCutHz = 250, HighCutHz = 6000, Drive = 1.6f };
    }

    /// <summary>Range filters, a three band EQ and a soft clipper over interleaved samples. Runs after volume and distance, so the clipper sees playback loudness.</summary>
    public class ToneChain
    {
        public const float LowsHz = 200f;
        public const float MidsHz = 1000f;
        public const float HighsHz = 4000f;
        private const double MidsQ = 0.7;

        private readonly int _sampleRate;
        private readonly int _channels;
        private readonly Channel[] _state;
        // Settings the filters were last configured for.
        private float _lowCut = float.NaN, _highCut = float.NaN, _lows = float.NaN, _mids = float.NaN, _highs = float.NaN;

        public ToneChain(int sampleRate, int channels, ToneSettings settings)
        {
            _sampleRate = sampleRate;
            _channels = channels;
            Settings = settings;
            _state = new Channel[channels];
            for (int i = 0; i < channels; i++)
                _state[i] = new Channel();
        }

        public ToneSettings Settings { get; }

        public int Channels => _channels;

        public void Process(float[] data)
        {
            Configure();
            var s = Settings;
            float drive = Math.Max(0.01f, s.Drive);
            // A clean speaker bends only past the knee, since tanh over the whole range squashed loud songs.
            bool rough = drive > 1f;
            for (int frame = 0; frame + _channels <= data.Length; frame += _channels)
            {
                for (int c = 0; c < _channels; c++)
                {
                    var ch = _state[c];
                    float x = data[frame + c];
                    if (ch.LowCut != null) x = ch.LowCut.Process(x);
                    if (ch.HighCut != null) x = ch.HighCut.Process(x);
                    x = ch.Lows.Process(x);
                    x = ch.Mids.Process(x);
                    x = ch.Highs.Process(x);
                    data[frame + c] = rough ? (float)Math.Tanh(drive * x) / drive : SoftKnee.Clip(x);
                }
            }
        }

        private void Configure()
        {
            var s = Settings;
            if (s.LowCutHz != _lowCut)
            {
                _lowCut = s.LowCutHz;
                foreach (var ch in _state)
                    ch.LowCut = _lowCut > 0 ? (ch.LowCut ?? new Biquad()).SetHighPass(_sampleRate, _lowCut, 0.7071) : null;
            }
            if (s.HighCutHz != _highCut)
            {
                _highCut = s.HighCutHz;
                foreach (var ch in _state)
                    ch.HighCut = _highCut > 0 ? (ch.HighCut ?? new Biquad()).SetLowPass(_sampleRate, _highCut, 0.7071) : null;
            }
            if (s.LowsDb != _lows)
            {
                _lows = s.LowsDb;
                foreach (var ch in _state)
                    ch.Lows.SetLowShelf(_sampleRate, LowsHz, _lows);
            }
            if (s.MidsDb != _mids)
            {
                _mids = s.MidsDb;
                foreach (var ch in _state)
                    ch.Mids.SetPeak(_sampleRate, MidsHz, MidsQ, _mids);
            }
            if (s.HighsDb != _highs)
            {
                _highs = s.HighsDb;
                foreach (var ch in _state)
                    ch.Highs.SetHighShelf(_sampleRate, HighsHz, _highs);
            }
        }

        private class Channel
        {
            public Biquad LowCut;
            public Biquad HighCut;
            public readonly Biquad Lows = new Biquad();
            public readonly Biquad Mids = new Biquad();
            public readonly Biquad Highs = new Biquad();
        }
    }
}
