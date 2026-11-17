using System;

namespace DVRAT
{
    /// <summary>
    /// The last second or so of a track as mono, indexed from the track's start. The track reads ahead of the
    /// speakers, so the spectrum is taken at the playback position. Written on the reader thread, read on the main thread.
    /// </summary>
    public class SampleHistory
    {
        private readonly float[] _ring;
        private readonly object _lock = new object();
        private long _written;

        public SampleHistory(int capacity)
        {
            _ring = new float[capacity];
        }

        /// <summary>Since the start or the last <see cref="Clear"/>.</summary>
        public long Written
        {
            get { lock (_lock) return _written; }
        }

        public void Write(float[] interleaved, int count, int channels)
        {
            lock (_lock)
            {
                for (int frame = 0; frame + channels <= count; frame += channels)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                        sum += interleaved[frame + c];
                    _ring[_written++ % _ring.Length] = sum / channels;
                }
            }
        }

        /// <summary>Fills the window with the samples just before <paramref name="end"/>. False if not all are available (too early or overwritten).</summary>
        public bool Read(long end, float[] window)
        {
            lock (_lock)
            {
                long start = end - window.Length;
                if (start < 0 || end > _written || start < _written - _ring.Length) return false;
                for (int i = 0; i < window.Length; i++)
                    window[i] = _ring[(start + i) % _ring.Length];
                return true;
            }
        }

        public void Clear()
        {
            lock (_lock)
                _written = 0;
        }
    }

    /// <summary>Splits mono samples into log-spaced bands, each a level from 0 (-60 dB) to 1 (a full-scale tone).</summary>
    public class SpectrumAnalyzer
    {
        /// <summary>At 48 kHz that is 43 ms, with bins 23 Hz apart.</summary>
        public const int Size = 2048;
        public const float FloorDb = -60f;
        public const float MinHz = 40f;
        public const float MaxHz = 16000f;

        private readonly float[] _hann = new float[Size];
        private readonly float[] _re = new float[Size];
        private readonly float[] _im = new float[Size];
        private readonly int[] _first, _last;

        public SpectrumAnalyzer(int sampleRate, int bands)
        {
            for (int i = 0; i < Size; i++)
                _hann[i] = 0.5f * (1f - (float)Math.Cos(2 * Math.PI * i / (Size - 1)));
            _first = new int[bands];
            _last = new int[bands];
            double binHz = (double)sampleRate / Size;
            for (int b = 0; b < bands; b++)
            {
                double from = MinHz * Math.Pow(MaxHz / MinHz, (double)b / bands);
                double to = MinHz * Math.Pow(MaxHz / MinHz, (double)(b + 1) / bands);
                _first[b] = Math.Max(1, (int)Math.Round(from / binHz));
                // Low bands are narrower than a bin, so each gets at least one.
                _last[b] = Math.Max(_first[b], Math.Min(Size / 2 - 1, (int)Math.Round(to / binHz) - 1));
            }
        }

        public int Bands => _first.Length;

        /// <param name="samples"><see cref="Size"/> mono samples.</param>
        public void Analyze(float[] samples, float[] levels)
        {
            for (int i = 0; i < Size; i++)
            {
                _re[i] = samples[i] * _hann[i];
                _im[i] = 0f;
            }
            Fft(_re, _im);
            // A Hann window halves a tone's amplitude. This restores a full-scale tone to 1.
            const float scale = 4f / Size;
            for (int b = 0; b < _first.Length; b++)
            {
                float peak = 0f;
                for (int k = _first[b]; k <= _last[b]; k++)
                    peak = Math.Max(peak, (float)Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * scale);
                float db = peak > 0f ? 20f * (float)Math.Log10(peak) : FloorDb;
                levels[b] = Math.Max(0f, Math.Min(1f, (db - FloorDb) / -FloorDb));
            }
        }

        private static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;
                j ^= bit;
                if (i >= j) continue;
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2 * Math.PI / length;
                float wRe = (float)Math.Cos(angle), wIm = (float)Math.Sin(angle);
                for (int i = 0; i < n; i += length)
                {
                    float uRe = 1f, uIm = 0f;
                    for (int k = 0; k < length / 2; k++)
                    {
                        int a = i + k, b = a + length / 2;
                        float tRe = re[b] * uRe - im[b] * uIm;
                        float tIm = re[b] * uIm + im[b] * uRe;
                        re[b] = re[a] - tRe;
                        im[b] = im[a] - tIm;
                        re[a] += tRe;
                        im[a] += tIm;
                        float next = uRe * wRe - uIm * wIm;
                        uIm = uRe * wIm + uIm * wRe;
                        uRe = next;
                    }
                }
            }
        }
    }

    /// <summary>Bars rise at once and fall at <see cref="FallPerSecond"/>, so a beat stays readable.</summary>
    public class BarMotion
    {
        public const float FallPerSecond = 1.5f;

        public BarMotion(int bars)
        {
            Levels = new float[bars];
        }

        public float[] Levels { get; }

        public void Step(float[] target, float seconds)
        {
            for (int i = 0; i < Levels.Length; i++)
                Levels[i] = target[i] >= Levels[i] ? target[i] : Math.Max(target[i], Levels[i] - FallPerSecond * seconds);
        }
    }
}
