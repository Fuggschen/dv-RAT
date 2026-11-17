using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Which speakers and subwoofers a tablet connects to. No Unity types, so it can be tested.</summary>
    public static class DevicePicker
    {
        public const int MaxSpeakers = 5;
        public const int MaxSubwoofers = 1;

        /// <summary>Takes candidates in order, so list connected devices first: a new one never pushes out a connected one.</summary>
        public static List<T> Pick<T>(IEnumerable<T> candidates, Func<T, SpeakerKind> kind, Func<T, bool> usable)
        {
            var picked = new List<T>();
            int speakers = 0, subwoofers = 0;
            foreach (var device in candidates)
            {
                if (!usable(device) || picked.Contains(device)) continue;
                if (kind(device) == SpeakerKind.Subwoofer)
                {
                    if (subwoofers >= MaxSubwoofers) continue;
                    subwoofers++;
                }
                else
                {
                    if (speakers >= MaxSpeakers) continue;
                    speakers++;
                }
                picked.Add(device);
            }
            return picked;
        }
    }

    /// <summary>
    /// Feeds the same samples to several outputs, one queue each. Thread-safe: the decoder writes, audio callbacks read.
    /// An output idle for <see cref="StallMs"/> stops holding back the others and is realigned when it reads again.
    /// </summary>
    public class AudioFanOut
    {
        public const long StallMs = 500;

        private readonly int _capacity;
        private readonly Func<long> _nowMs;
        private readonly List<FloatRingBuffer> _outputs = new List<FloatRingBuffer>();
        private readonly Dictionary<FloatRingBuffer, long> _lastRead = new Dictionary<FloatRingBuffer, long>();
        private readonly object _lock = new object();
        private long _written;
        private int _lastLevel;

        public AudioFanOut(int capacity, Func<long> nowMs)
        {
            _capacity = capacity;
            _nowMs = nowMs;
        }

        /// <summary>Samples played so far, counting both channels.</summary>
        public long Played
        {
            get { lock (_lock) return _written - Level(); }
        }

        public bool Drained
        {
            get { lock (_lock) return Level() == 0; }
        }

        public FloatRingBuffer AddOutput()
        {
            var output = new FloatRingBuffer(_capacity);
            lock (_lock)
            {
                Align(output, Level());
                _outputs.Add(output);
                _lastRead[output] = _nowMs();
            }
            return output;
        }

        public void RemoveOutput(FloatRingBuffer output)
        {
            lock (_lock)
            {
                _outputs.Remove(output);
                _lastRead.Remove(output);
            }
        }

        /// <summary>Writes as many samples as the fullest playing queue takes, none while no output plays. Returns how many.</summary>
        public int Write(float[] samples, int offset, int count)
        {
            lock (_lock)
            {
                long now = _nowMs();
                int room = int.MaxValue;
                foreach (var output in _outputs)
                    if (IsLive(output, now))
                        room = Math.Min(room, output.Capacity - output.Count);
                if (room == int.MaxValue) return 0;

                int amount = Math.Min(count, room);
                foreach (var output in _outputs)
                    output.Write(samples, offset, amount);
                _written += amount;
                return amount;
            }
        }

        /// <summary>For the output's audio callback. Returns how many samples were read.</summary>
        public int Read(FloatRingBuffer output, float[] target, int offset, int count)
        {
            lock (_lock)
            {
                long now = _nowMs();
                if (_lastRead.TryGetValue(output, out long last) && now - last > StallMs)
                {
                    // Back after a stop: realign to the outputs that kept playing.
                    int level = Level(except: output);
                    if (level >= 0)
                        Align(output, level);
                }
                if (_lastRead.ContainsKey(output))
                    _lastRead[output] = now;
                return output.Read(target, offset, count);
            }
        }

        private bool IsLive(FloatRingBuffer output, long now) => now - _lastRead[output] <= StallMs;

        /// <summary>Queue level of the playing outputs, or the last level while none plays. With except, -1 when no other plays.</summary>
        private int Level(FloatRingBuffer except = null)
        {
            long now = _nowMs();
            foreach (var output in _outputs)
            {
                if (output == except || !IsLive(output, now)) continue;
                _lastLevel = output.Count;
                return _lastLevel;
            }
            return except != null ? -1 : _lastLevel;
        }

        /// <summary>Drops the oldest samples or pads with leading silence to reach the level.</summary>
        private static void Align(FloatRingBuffer output, int level)
        {
            int count = output.Count;
            if (count > level)
            {
                output.Read(new float[count - level], 0, count - level);
            }
            else if (count < level)
            {
                var kept = new float[count];
                output.Read(kept, 0, count);
                var silence = new float[level - count];
                output.Write(silence, 0, silence.Length);
                output.Write(kept, 0, kept.Length);
            }
        }
    }
}
