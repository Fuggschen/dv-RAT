using System;
using System.Collections.Generic;
using Concentus;
using Concentus.Enums;
using UnityEngine;

namespace DVRAT.Opus
{
    /// <summary>Opus at 48 kHz stereo, two 20 ms frames per packet, resampled to and from each PC's output rate. Pure C# Concentus, as the game has no native libopus.</summary>
    internal static class OpusAudio
    {
        public const int Rate = 48000;
        public const int Channels = 2;
        public const int FrameSamples = Rate / 50;
        public const int FramesPerPacket = 2;
        public const int Bitrate = 128000;
        // Opus's limit for one frame.
        public const int MaxFrameBytes = 1275;
        private const int ResampleQuality = 5;

        static OpusAudio()
        {
            OpusCodecFactory.AttemptToUseNativeLibrary = false;
        }

        /// <summary>Each frame is prefixed with its length in two bytes.</summary>
        public static byte[] Pack(List<byte[]> frames)
        {
            int length = 0;
            foreach (var frame in frames) length += 2 + frame.Length;
            var packet = new byte[length];
            int at = 0;
            foreach (var frame in frames)
            {
                packet[at++] = (byte)(frame.Length & 0xFF);
                packet[at++] = (byte)(frame.Length >> 8);
                Buffer.BlockCopy(frame, 0, packet, at, frame.Length);
                at += frame.Length;
            }
            return packet;
        }

        public static List<byte[]> Unpack(byte[] packet)
        {
            var frames = new List<byte[]>();
            int at = 0;
            while (at + 2 <= packet.Length)
            {
                int length = packet[at] | packet[at + 1] << 8;
                at += 2;
                if (length <= 0 || at + length > packet.Length) break;
                var frame = new byte[length];
                Buffer.BlockCopy(packet, at, frame, 0, length);
                frames.Add(frame);
                at += length;
            }
            return frames;
        }

        public static IOpusEncoder CreateEncoder()
        {
            var encoder = OpusCodecFactory.CreateEncoder(Rate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO, null);
            encoder.Bitrate = Bitrate;
            return encoder;
        }

        public static IOpusDecoder CreateDecoder() => OpusCodecFactory.CreateDecoder(Rate, Channels, null);

        /// <summary>Null when the rates match.</summary>
        public static IResampler Resampler(int from, int to) =>
            from == to ? null : ResamplerFactory.CreateResampler(Channels, from, to, ResampleQuality, null);

        public static float[] Resample(IResampler resampler, float[] samples, int count, int from, int to)
        {
            if (resampler == null)
            {
                var copy = new float[count];
                Array.Copy(samples, copy, count);
                return copy;
            }
            int inFrames = count / Channels;
            // Slack for the resampler's rounding.
            var output = new float[((long)inFrames * to / from + 16) * Channels];
            int inLength = inFrames;
            int outLength = output.Length / Channels;
            resampler.ProcessInterleaved(new Span<float>(samples, 0, count), ref inLength, output, ref outLength);
            var result = new float[outLength * Channels];
            Array.Copy(output, result, result.Length);
            return result;
        }
    }

    /// <summary>Created through reflection by DVRAT.Multiplayer.dll.</summary>
    public sealed class OpusEncoderSink : IAudioEncoder
    {
        private readonly Action<uint, uint, byte[]> _send;
        private readonly object _lock = new object();
        private readonly IOpusEncoder _opus = OpusAudio.CreateEncoder();
        private readonly int _localRate = AudioSettings.outputSampleRate;
        private IResampler _resampler;
        private readonly float[] _frame = new float[OpusAudio.FrameSamples * OpusAudio.Channels];
        private int _filled;
        private readonly List<byte[]> _frames = new List<byte[]>();
        private readonly byte[] _encoded = new byte[OpusAudio.MaxFrameBytes];
        private uint _epoch;
        private uint _seq;

        /// <param name="send">Called on the reader thread with epoch, sequence number and packet.</param>
        public OpusEncoderSink(Action<uint, uint, byte[]> send)
        {
            _send = send;
            _resampler = OpusAudio.Resampler(_localRate, OpusAudio.Rate);
        }

        public void Write(uint epoch, float[] samples, int count)
        {
            lock (_lock)
            {
                if (epoch != _epoch)
                {
                    // New song: drop all state from the last one.
                    _epoch = epoch;
                    _seq = 0;
                    _filled = 0;
                    _frames.Clear();
                    _opus.ResetState();
                    _resampler = OpusAudio.Resampler(_localRate, OpusAudio.Rate);
                }
                var at48 = OpusAudio.Resample(_resampler, samples, count, _localRate, OpusAudio.Rate);
                int at = 0;
                while (at < at48.Length)
                {
                    int n = Math.Min(_frame.Length - _filled, at48.Length - at);
                    Array.Copy(at48, at, _frame, _filled, n);
                    _filled += n;
                    at += n;
                    if (_filled < _frame.Length) break;
                    _filled = 0;
                    int length = _opus.Encode(_frame, OpusAudio.FrameSamples, _encoded, _encoded.Length);
                    var frame = new byte[length];
                    Buffer.BlockCopy(_encoded, 0, frame, 0, length);
                    _frames.Add(frame);
                    if (_frames.Count < OpusAudio.FramesPerPacket) continue;
                    _send(_epoch, _seq++, OpusAudio.Pack(_frames));
                    _frames.Clear();
                }
            }
        }
    }

    /// <summary>Created through reflection by DVRAT.Multiplayer.dll.</summary>
    public sealed class OpusDecoderSource : IAudioDecoder
    {
        private readonly IOpusDecoder _opus = OpusAudio.CreateDecoder();
        private readonly int _localRate = AudioSettings.outputSampleRate;
        private readonly IResampler _resampler;
        private readonly float[] _frame = new float[OpusAudio.FrameSamples * OpusAudio.Channels];

        public OpusDecoderSource()
        {
            _resampler = OpusAudio.Resampler(OpusAudio.Rate, _localRate);
        }

        public float[] Decode(byte[] packet)
        {
            var decoded = new List<float>(OpusAudio.FrameSamples * OpusAudio.Channels * OpusAudio.FramesPerPacket);
            if (packet == null)
            {
                // Lost packet: Opus conceals the missing frames.
                for (int i = 0; i < OpusAudio.FramesPerPacket; i++)
                    Add(decoded, _opus.Decode(ReadOnlySpan<byte>.Empty, _frame, OpusAudio.FrameSamples, false));
            }
            else
                foreach (var frame in OpusAudio.Unpack(packet))
                    Add(decoded, _opus.Decode(frame, _frame, OpusAudio.FrameSamples, false));
            var samples = decoded.ToArray();
            return OpusAudio.Resample(_resampler, samples, samples.Length, OpusAudio.Rate, _localRate);
        }

        private void Add(List<float> to, int frameSamples)
        {
            for (int i = 0; i < frameSamples * OpusAudio.Channels; i++)
                to.Add(_frame[i]);
        }
    }
}
