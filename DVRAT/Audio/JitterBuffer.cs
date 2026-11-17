using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Reorders unreliable sound packets from another player's tablet. Thread-safe: pushed on the main thread, taken on the player's.</summary>
    public class JitterBuffer
    {
        /// <summary>Packets held before playing starts, and again after running dry (200 ms).</summary>
        public const int StartPackets = 5;

        /// <summary>Above this many waiting, the oldest are dropped (about 0.5 s).</summary>
        public const int MaxPackets = 13;

        private readonly SortedDictionary<uint, byte[]> _packets = new SortedDictionary<uint, byte[]>();
        private readonly object _lock = new object();
        private uint _epoch;
        private uint _next;
        private bool _started;
        private bool _playing;

        /// <summary>Packets of an older epoch are dropped.</summary>
        public uint Epoch
        {
            get { lock (_lock) return _epoch; }
        }

        public int Count
        {
            get { lock (_lock) return _packets.Count; }
        }

        public void Push(uint epoch, uint seq, byte[] packet)
        {
            lock (_lock)
            {
                if (epoch < _epoch) return;
                if (epoch > _epoch)
                {
                    _epoch = epoch;
                    _packets.Clear();
                    _started = false;
                    _playing = false;
                }
                // Too late, already played past.
                if (_started && seq < _next) return;
                _packets[seq] = packet;
                while (_packets.Count > MaxPackets)
                {
                    uint oldest = First();
                    _packets.Remove(oldest);
                    if (_started && oldest >= _next) _next = oldest + 1;
                }
                if (!_started)
                    _next = First();
            }
        }

        /// <summary>True with a null packet for a missing one (a gap), false while nothing is ready.</summary>
        public bool TryTake(out byte[] packet)
        {
            lock (_lock)
            {
                packet = null;
                if (!_playing)
                {
                    if (_packets.Count < StartPackets) return false;
                    _playing = true;
                    if (!_started)
                    {
                        _started = true;
                        _next = First();
                    }
                }
                if (_packets.Count == 0)
                {
                    // Ran dry: wait for a new head start.
                    _playing = false;
                    return false;
                }
                if (_packets.TryGetValue(_next, out packet))
                    _packets.Remove(_next);
                _next++;
                return true;
            }
        }

        // Call with the lock held and at least one packet.
        private uint First()
        {
            foreach (uint seq in _packets.Keys)
                return seq;
            return 0;
        }
    }
}
