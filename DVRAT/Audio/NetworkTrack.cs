using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Plays what another player's tablet plays, from <see cref="JitterBuffer"/> packets, through the same sources as a song.</summary>
    public class NetworkTrack : PcmProcessTrack
    {
        private readonly PacketStream _stream;

        public NetworkTrack(JitterBuffer buffer, IAudioDecoder decoder)
        {
            _stream = new PacketStream(buffer, decoder);
        }

        /// <summary>The next song comes on a fresh track, so it has no tail of the old one.</summary>
        public uint Epoch => _stream.Epoch;

        public void Start(string name, IEnumerable<AudioSource> sources) => LaunchStream(() => _stream, name, sources);

        protected override void EndGracefully() => _stream.End();

        protected override void OnErrorLine(string line)
        {
        }

        protected override string Failure(int exitCode) => null;

        // Only ends when the track is stopped.
        protected override string StreamEnded() => null;

        private class PacketStream : Stream
        {
            private const int WaitMs = 5;

            private readonly JitterBuffer _buffer;
            private readonly IAudioDecoder _decoder;
            private byte[] _pending = new byte[0];
            private int _pendingAt;
            private volatile bool _closed;

            public PacketStream(JitterBuffer buffer, IAudioDecoder decoder)
            {
                _buffer = buffer;
                _decoder = decoder;
                Epoch = buffer.Epoch;
            }

            public uint Epoch { get; }

            public void End() => _closed = true;

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (_pendingAt >= _pending.Length)
                {
                    if (_closed) return 0;
                    if (!_buffer.TryTake(out byte[] packet))
                    {
                        Thread.Sleep(WaitMs);
                        continue;
                    }
                    float[] samples = _decoder.Decode(packet);
                    _pending = new byte[samples.Length * sizeof(float)];
                    Buffer.BlockCopy(samples, 0, _pending, 0, _pending.Length);
                    _pendingAt = 0;
                }
                int n = Math.Min(count, _pending.Length - _pendingAt);
                Buffer.BlockCopy(_pending, _pendingAt, buffer, offset, n);
                _pendingAt += n;
                return n;
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
}
