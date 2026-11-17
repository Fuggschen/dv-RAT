using System;
using System.IO;
using System.Text;

namespace DVRAT
{
    /// <summary>Exposes the helper's sound frames as a raw stream and passes line frames to the callback.</summary>
    public sealed class FrameReader : Stream
    {
        private readonly Stream _inner;
        private readonly Action<string> _onLine;
        private readonly byte[] _header = new byte[5];
        private int _soundLeft;

        public FrameReader(Stream inner, Action<string> onLine)
        {
            _inner = inner;
            _onLine = onLine;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            while (_soundLeft == 0)
            {
                if (!ReadExactly(_header, 5)) return 0;
                int length = _header[1] | _header[2] << 8 | _header[3] << 16 | _header[4] << 24;
                if (length < 0) throw new IOException("Broken frame from the Linux helper");
                if (_header[0] == (byte)'A')
                {
                    _soundLeft = length;
                    continue;
                }
                var payload = new byte[length];
                if (!ReadExactly(payload, length)) return 0;
                if (_header[0] == (byte)'S')
                    _onLine(Encoding.UTF8.GetString(payload));
            }
            int got = _inner.Read(buffer, offset, Math.Min(count, _soundLeft));
            if (got <= 0) return 0;
            _soundLeft -= got;
            return got;
        }

        private bool ReadExactly(byte[] buffer, int length)
        {
            int done = 0;
            while (done < length)
            {
                int got = _inner.Read(buffer, done, length - done);
                if (got <= 0) return false;
                done += got;
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
