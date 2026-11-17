using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Plays a music file or radio stream decoded by the bundled ffmpeg.exe. The only class that starts it.</summary>
    public class FfmpegTrack : PcmProcessTrack
    {
        /// <summary>Formats only ffmpeg plays. The bundled build decodes exactly these and <see cref="MediaPlayer.GameExtensions"/>.</summary>
        public static readonly string[] Extensions = { ".m4a", ".aac", ".flac", ".wma", ".opus", ".ape", ".mka", ".webm" };

        private const int MaxQueuedBytes = 1 << 20;
        private readonly Queue<byte[]> _input = new Queue<byte[]>();
        private int _queuedBytes;
        private bool _inputEnded;
        private Thread _writer;
        // Radio only.
        private RadioBuffering _buffering;
        private Func<float> _signal;
        private volatile string _problem;
        // Seconds; 0 while unknown.
        private volatile float _duration;

        public static string ExePath =>
            RATMain.ModEntry != null ? Path.Combine(RATMain.ModEntry.Path, "ffmpeg", "ffmpeg.exe") : null;

        /// <summary>False when ffmpeg.exe is missing, e.g. removed by a virus scanner. Only mp3, ogg and wav play then.</summary>
        public static bool Available => ExePath != null && File.Exists(ExePath);

        /// <summary>True while a radio stream waits for its buffer to fill.</summary>
        public bool Buffering => _buffering != null && _buffering.Buffering;

        /// <summary>0 until ffmpeg has opened the file, or when the file does not say.</summary>
        public float DurationSeconds => _duration;

        public void Start(string path, IEnumerable<AudioSource> sources) =>
            LaunchFfmpeg(Path.GetFileNameWithoutExtension(path), $"-nostdin -i \"{path}\"", sources, fromInput: false);

        /// <summary>Decodes a radio stream fed through <see cref="WriteInput"/>.</summary>
        /// <param name="format">ffmpeg's format name, which skips probing; null to probe.</param>
        /// <param name="signal">Radio signal 0 to 1, read on the decoder thread; null for clean sound.</param>
        public void StartStream(string name, string format, IEnumerable<AudioSource> sources, Func<float> signal)
        {
            if (signal != null)
            {
                _signal = signal;
                _buffering = new RadioBuffering(AudioSettings.outputSampleRate, Channels);
            }
            string formatArgs = format != null ? $"-f {format} " : "";
            LaunchFfmpeg(name, $"-probesize 65536 -analyzeduration 500000 {formatArgs}-i pipe:0", sources, fromInput: true);
            _writer = new Thread(WriteInputLoop) { IsBackground = true, Name = "RAT ffmpeg input" };
            _writer.Start();
        }

        /// <summary>Drops the oldest bytes when more than <see cref="MaxQueuedBytes"/> wait, e.g. while the game is paused.</summary>
        public void WriteInput(byte[] data, int offset, int count)
        {
            if (count <= 0) return;
            var copy = new byte[count];
            Buffer.BlockCopy(data, offset, copy, 0, count);
            lock (_input)
            {
                _input.Enqueue(copy);
                _queuedBytes += count;
                while (_queuedBytes > MaxQueuedBytes && _input.Count > 1)
                    _queuedBytes -= _input.Dequeue().Length;
                Monitor.Pulse(_input);
            }
        }

        /// <summary>ffmpeg finishes the queued bytes, then ends.</summary>
        public void EndInput()
        {
            lock (_input)
            {
                _inputEnded = true;
                Monitor.Pulse(_input);
            }
        }

        private void LaunchFfmpeg(string name, string inputArgs, IEnumerable<AudioSource> sources, bool fromInput) =>
            Launch(ExePath,
                $"-hide_banner -nostats -loglevel level+info {inputArgs} -map 0:a:0 -vn -ac {Channels} -ar {AudioSettings.outputSampleRate} -f f32le -",
                name, sources, fromInput);

        // The last error or fatal line says why it failed.
        protected override void OnErrorLine(string line)
        {
            double? duration = FfmpegLog.Duration(line);
            if (duration.HasValue && _duration == 0f)
                _duration = (float)duration.Value;
            _problem = FfmpegLog.Problem(line) ?? _problem;
        }

        protected override string Failure(int exitCode) => _problem ?? $"ffmpeg ended with code {exitCode}";

        protected override void Shape(float[] samples, int count) => _buffering?.Process(samples, 0, count, _signal());

        protected override void BeforeKill()
        {
            lock (_input)
                Monitor.Pulse(_input);
        }

        protected override void AfterKill() => _writer?.Join(StopTimeoutMs);

        private void WriteInputLoop()
        {
            try
            {
                var stdin = Process.StandardInput.BaseStream;
                while (true)
                {
                    byte[] chunk;
                    lock (_input)
                    {
                        while (_input.Count == 0 && !_inputEnded && !Stopping)
                            Monitor.Wait(_input);
                        if (Stopping || _input.Count == 0) break;
                        chunk = _input.Dequeue();
                        _queuedBytes -= chunk.Length;
                    }
                    stdin.Write(chunk, 0, chunk.Length);
                    stdin.Flush();
                }
                stdin.Close();
            }
            catch (Exception)
            {
                // ffmpeg ended or was killed.
            }
        }
    }
}
