using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Shuffling keeps the current song first. Turning it off continues in folder order from the current song.</summary>
    public class PlayQueue
    {
        private readonly Random _random;
        private int[] _order;
        private int _position;
        private bool _shuffle;

        public PlayQueue(IReadOnlyList<string> files, int start, bool shuffle, Random random)
        {
            if (files == null || files.Count == 0) throw new ArgumentException("A queue needs at least one file", nameof(files));
            if (start < 0 || start >= files.Count) throw new ArgumentOutOfRangeException(nameof(start));
            Files = files;
            _random = random;
            _order = InOrder();
            _position = start;
            Shuffle = shuffle;
        }

        public IReadOnlyList<string> Files { get; }

        public string Current => Files[_order[_position]];

        public bool HasNext => _position + 1 < _order.Length;

        public bool Shuffle
        {
            get => _shuffle;
            set
            {
                if (value == _shuffle) return;
                _shuffle = value;
                int current = _order[_position];
                _order = InOrder();
                if (value)
                {
                    // Fisher-Yates over everything after the current song.
                    _order[current] = 0;
                    _order[0] = current;
                    for (int i = _order.Length - 1; i > 1; i--)
                    {
                        int j = 1 + _random.Next(i);
                        (_order[i], _order[j]) = (_order[j], _order[i]);
                    }
                    _position = 0;
                }
                else
                {
                    _position = current;
                }
            }
        }

        /// <summary>False after the last song, which stays current.</summary>
        public bool MoveNext()
        {
            if (_position + 1 >= _order.Length) return false;
            _position++;
            return true;
        }

        /// <summary>False at the first song, which stays current.</summary>
        public bool MovePrevious()
        {
            if (_position == 0) return false;
            _position--;
            return true;
        }

        private int[] InOrder()
        {
            var order = new int[Files.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            return order;
        }
    }
}
