using System;

namespace DVRAT
{
    /// <summary>Sample queue between a writer thread (decoder) and a reader thread (audio callback). Never blocks beyond the short lock.</summary>
    public class FloatRingBuffer
    {
        private readonly float[] _data;
        private readonly object _lock = new object();
        private int _start;
        private int _count;

        public FloatRingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _data = new float[capacity];
        }

        public int Capacity => _data.Length;

        public int Count
        {
            get { lock (_lock) return _count; }
        }

        /// <summary>Returns how many samples fit and were written.</summary>
        public int Write(float[] source, int offset, int count)
        {
            lock (_lock)
            {
                int written = Math.Min(count, _data.Length - _count);
                int end = (_start + _count) % _data.Length;
                int first = Math.Min(written, _data.Length - end);
                Array.Copy(source, offset, _data, end, first);
                Array.Copy(source, offset + first, _data, 0, written - first);
                _count += written;
                return written;
            }
        }

        /// <summary>Returns how many samples were available and read.</summary>
        public int Read(float[] target, int offset, int count)
        {
            lock (_lock)
            {
                int read = Math.Min(count, _count);
                int first = Math.Min(read, _data.Length - _start);
                Array.Copy(_data, _start, target, offset, first);
                Array.Copy(_data, 0, target, offset + first, read - first);
                _start = (_start + read) % _data.Length;
                _count -= read;
                return read;
            }
        }
    }
}
