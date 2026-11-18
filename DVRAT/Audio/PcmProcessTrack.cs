using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Plays raw sound from another process's stdout (32-bit float, interleaved stereo at the output rate) through
    /// several AudioSources. Under Wine the sound can come over a socket instead, see <see cref="LaunchStream"/>.
    /// </summary>
    public abstract class PcmProcessTrack
    {
        protected const int Channels = 2;
        // More than Unity reads ahead of a new streamed clip (about half a second per clip).
        private const float BufferSeconds = 2f;
        // How long Fill waits for sound on Unity's streaming thread.
        private const int FillWaitMs = 300;
        // A streamed clip needs a length.
        private const int ClipHours = 10;
        protected const int StopTimeoutMs = 500;
        // Time for every source to be set up before the common start.
        private const double StartDelaySeconds = 0.1;
        // Live sound: queued before the sources start. More than Unity reads ahead of a new clip, which 0.75 s was not.
        private const float LiveStartSeconds = 1.5f;

        private AudioFanOut _fanOut;
        private Process _process;
        private Thread _reader;
        private string _name;
        private int _sampleRate;
        private bool _paused;
        private int _mainThread;
        // Where the clips began, in the queue and on the mixer's clock, and the time paused since.
        private long _clipStartFrame;
        private double _clipStartDsp;
        private double _pausedSeconds;
        private double _pausedAtDsp = -1;
        private double _outputLatency = -1;
        private volatile bool _stopping;
        // Its last samples are read and dropped so it never blocks on a full stdout.
        private volatile bool _draining;
        private volatile bool _finished;
        private volatile string _error;
        // Live sound: a source ran short since the last start, and when the hold began.
        private volatile bool _ranDry;
        private float _holdSince;
        // Counts StartTogether calls; a clip from an earlier one reads nothing. Held with the read that checks it.
        private int _starts;
        private readonly object _startLock = new object();
        // Live sound: the first short read since the last start, logged by Update.
        private long _startedMs;
        private string _firstShortRead;

        private readonly List<Output> _outputs = new List<Output>();
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Gets every sample on the reader thread, for multiplayer sync. Set before the track starts.</summary>
        public Action<float[], int> Tap { get; set; }

        /// <summary>Null while all is well.</summary>
        public string Error => _error;

        /// <summary>True once the process has ended and the playing sources have played every sample.</summary>
        public bool Ended => _finished && _error == null && _fanOut.Drained;

        /// <summary>Samples per channel handed to the speakers so far.</summary>
        /// <remarks>
        /// Uses the mixer's clock, since Unity counts samples as played up to half a second early. Main thread only.
        /// </remarks>
        public long PlayedFrames
        {
            get
            {
                if (_outputs.Count == 0 || _sampleRate <= 0)
                    return _fanOut != null ? _fanOut.Played / Channels : 0;
                if (_outputLatency < 0)
                {
                    AudioSettings.GetDSPBufferSize(out int length, out int buffers);
                    _outputLatency = (double)length * buffers / _sampleRate;
                }
                double now = _pausedAtDsp >= 0 ? _pausedAtDsp : AudioSettings.dspTime;
                double played = Math.Max(0, now - _clipStartDsp - _pausedSeconds - _outputLatency);
                // Never past what Unity has read.
                return Math.Min(_clipStartFrame + (long)(played * _sampleRate), _fanOut.Played / Channels);
            }
        }

        public float PlayedSeconds => _sampleRate > 0 && _fanOut != null ? _fanOut.Played / (float)(_sampleRate * Channels) : 0f;

        protected Process Process => _process;
        protected bool Stopping => _stopping;
        protected int SampleRate => _sampleRate;

        protected void Launch(string exe, string arguments, string name, IEnumerable<AudioSource> sources, bool redirectInput,
            Encoding errorEncoding = null)
        {
            Prepare(name, sources);
            var start = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (errorEncoding != null)
                start.StandardErrorEncoding = errorEncoding;
            var process = Process.Start(start) ?? throw new InvalidOperationException(exe + " did not start");
            _process = process;

            // The reader keeps its own reference because Stop may hand the process to another thread.
            _reader = new Thread(() => ReadSamples(process)) { IsBackground = true, Name = "RAT " + name + " reader" };
            _reader.Start();
        }

        /// <summary><paramref name="open"/> runs on the reader thread and may block. Its exception becomes <see cref="Error"/>.</summary>
        protected void LaunchStream(Func<Stream> open, string name, IEnumerable<AudioSource> sources)
        {
            Prepare(name, sources);
            _reader = new Thread(() =>
            {
                try
                {
                    PlayFrom(open());
                    if (_stopping) return;
                    _error = StreamEnded();
                    _finished = true;
                }
                catch (Exception ex) when (!_stopping)
                {
                    _error = ex.Message;
                    _finished = true;
                }
                catch (Exception)
                {
                    // Stop closed the stream while it was being read.
                }
            }) { IsBackground = true, Name = "RAT " + name + " reader" };
            _reader.Start();
        }

        private void Prepare(string name, IEnumerable<AudioSource> sources)
        {
            _sampleRate = AudioSettings.outputSampleRate;
            _name = name;
            _fanOut = new AudioFanOut((int)(_sampleRate * Channels * BufferSeconds), () => Clock.ElapsedMilliseconds);
            foreach (var source in sources)
                Add(source);
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            if (Live) Hold();
            StartTogether();
        }

        /// <summary>
        /// True for sound that comes in real time, which never fills Unity's read-ahead of a new clip by itself. Sources then
        /// play silence until <see cref="LiveStartSeconds"/> is queued and restart together, at the start and after running dry.
        /// </summary>
        protected virtual bool Live => false;

        /// <summary>Call every frame on the main thread.</summary>
        public void Update()
        {
            if (!Live || _fanOut == null || _paused) return;
            if (!_fanOut.Holding)
            {
                if (!_ranDry) return;
                RATMain.Log($"{_name}: a speaker ran short ({_firstShortRead}); buffering again");
                Hold();
                return;
            }
            int target = (int)(_sampleRate * Channels * LiveStartSeconds);
            if (_fanOut.Queued < target && !_finished) return;
            StartTogether(() =>
            {
                _fanOut.Release(target);
                _ranDry = false;
                _firstShortRead = null;
            });
            RATMain.Log($"{_name}: speakers restarted together after {Time.unscaledTime - _holdSince:0.0} s of buffering");
        }

        private void Hold()
        {
            _fanOut.Hold();
            _holdSince = Time.unscaledTime;
        }

        /// <summary>Why a <see cref="LaunchStream"/> stream ended by itself; null when that is no error.</summary>
        protected virtual string StreamEnded() => null;

        /// <summary>Runs on a worker thread.</summary>
        protected abstract void OnErrorLine(string line);

        /// <summary>Called after the error output was read.</summary>
        protected abstract string Failure(int exitCode);

        /// <summary>Changes samples before they go to the outputs, on the reader thread.</summary>
        protected virtual void Shape(float[] samples, int count)
        {
        }

        /// <summary>Called first by <see cref="Stop"/>, while stdout is still read, so the process can end by itself.</summary>
        protected virtual void EndGracefully()
        {
        }

        /// <summary>Reads and drops the process's samples until it ends.</summary>
        protected void DropSamples() => _draining = true;

        /// <summary>More than 0: <see cref="Stop"/> does not wait, and the process is killed after this long.</summary>
        protected virtual int EndsByItselfWithinMs => 0;

        /// <summary>Called by <see cref="Stop"/> before the kill.</summary>
        protected virtual void BeforeKill()
        {
        }

        /// <summary>Called by <see cref="Stop"/> after the process is gone.</summary>
        protected virtual void AfterKill()
        {
        }

        /// <summary>Restarts every source together, since a new clip has nothing read ahead. Skips a few samples.</summary>
        public void AddOutput(AudioSource source)
        {
            if (!Add(source)) return;
            StartTogether();
        }

        private bool Add(AudioSource source)
        {
            if (source == null || _outputs.Exists(o => ReferenceEquals(o.Source, source))) return false;
            _outputs.Add(new Output { Source = source, Queue = _fanOut.AddOutput() });
            return true;
        }

        /// <param name="beforeStart">Runs once the old clips can no longer read, before the new ones exist.</param>
        private void StartTogether(Action beforeStart = null)
        {
            foreach (var output in _outputs)
            {
                if (output.Source == null) continue;
                output.Source.Stop();
                if (output.Clip != null)
                    UnityEngine.Object.Destroy(output.Clip);
            }
            // A stopped clip's read can still be under way on the streaming thread.
            int start;
            lock (_startLock)
            {
                start = ++_starts;
                beforeStart?.Invoke();
                _startedMs = Clock.ElapsedMilliseconds;
            }

            _clipStartFrame = _fanOut.Played / Channels;
            int lengthSamples = (int)Math.Min(int.MaxValue, (long)_sampleRate * 3600 * ClipHours);
            double startAt = AudioSettings.dspTime + StartDelaySeconds;
            _clipStartDsp = startAt;
            _pausedSeconds = 0;
            _pausedAtDsp = _paused ? startAt : -1;
            foreach (var output in _outputs)
            {
                if (output.Source == null) continue;
                var queue = output.Queue;
                output.Clip = AudioClip.Create(_name, lengthSamples, Channels, _sampleRate, true, data => Fill(queue, data, start));
                output.Source.clip = output.Clip;
                output.Source.PlayScheduled(startAt);
                if (_paused)
                    output.Source.Pause();
            }
        }

        public void RemoveOutput(AudioSource source)
        {
            // By reference, since Unity's == treats all destroyed sources as equal.
            var output = _outputs.Find(o => ReferenceEquals(o.Source, source));
            if (output == null) return;
            _outputs.Remove(output);
            Close(output);
        }

        /// <summary>Drops outputs whose source was destroyed.</summary>
        public void RemoveDestroyedOutputs()
        {
            for (int i = _outputs.Count - 1; i >= 0; i--)
            {
                if (_outputs[i].Source != null) continue;
                Close(_outputs[i]);
                _outputs.RemoveAt(i);
            }
        }

        public virtual void Pause()
        {
            if (!_paused) _pausedAtDsp = AudioSettings.dspTime;
            _paused = true;
            foreach (var output in _outputs)
                if (output.Source != null)
                    output.Source.Pause();
        }

        public virtual void Resume()
        {
            if (_paused && _pausedAtDsp >= 0)
            {
                _pausedSeconds += Math.Max(0, AudioSettings.dspTime - _pausedAtDsp);
                _pausedAtDsp = -1;
            }
            _paused = false;
            foreach (var output in _outputs)
                if (output.Source != null)
                    output.Source.UnPause();
        }

        public void Stop()
        {
            EndGracefully();
            _stopping = true;
            BeforeKill();
            var process = _process;
            var reader = _reader;
            _process = null;
            int grace = EndsByItselfWithinMs;
            if (process != null && grace > 0)
            {
                new Thread(() =>
                {
                    if (!process.WaitForExit(grace))
                        Kill(process);
                    reader?.Join(StopTimeoutMs);
                    process.Dispose();
                }) { IsBackground = true, Name = "RAT " + _name + " end" }.Start();
            }
            else
            {
                if (process != null)
                    Kill(process);
                reader?.Join(StopTimeoutMs);
                AfterKill();
                process?.Dispose();
            }

            foreach (var output in _outputs)
                Close(output);
            _outputs.Clear();
        }

        private static void Kill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                // It ended between the check and the kill.
            }
        }

        private void Close(Output output)
        {
            _fanOut?.RemoveOutput(output.Queue);
            // A speaker in the inventory has already destroyed its source.
            if (output.Source != null)
            {
                output.Source.Stop();
                if (output.Source.clip == output.Clip)
                    output.Source.clip = null;
            }
            if (output.Clip != null)
                UnityEngine.Object.Destroy(output.Clip);
        }

        /// <summary>Unity's audio callback for one source.</summary>
        private void Fill(FloatRingBuffer queue, float[] data, int start)
        {
            if (Live)
            {
                FillLive(queue, data, start);
                return;
            }
            if (start != Volatile.Read(ref _starts))
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            int read = _fanOut.Read(queue, data, 0, data.Length);
            // Unity reads a new clip far ahead on its streaming thread, and silence there plays later as a gap, so wait.
            if (read < data.Length && Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                long until = Clock.ElapsedMilliseconds + FillWaitMs;
                while (read < data.Length && !_stopping && !_finished && !_paused && Clock.ElapsedMilliseconds < until)
                {
                    Thread.Sleep(2);
                    read += _fanOut.Read(queue, data, read, data.Length - read);
                }
            }
            Array.Clear(data, read, data.Length - read);
        }

        // No waiting: one sleeping callback can hold up the other sources' refills. Update rebuffers instead.
        private void FillLive(FloatRingBuffer queue, float[] data, int start)
        {
            int read = 0;
            lock (_startLock)
            {
                if (start == _starts)
                {
                    bool holding = _fanOut.Holding;
                    int queued = queue.Count;
                    read = _fanOut.Read(queue, data, 0, data.Length);
                    if (read < data.Length && !holding && !_ranDry)
                    {
                        string thread = Thread.CurrentThread.ManagedThreadId == _mainThread ? "main" : "streaming";
                        _firstShortRead = $"{Clock.ElapsedMilliseconds - _startedMs} ms after the start, asked {data.Length}, " +
                                          $"got {read} of {queued} queued, on the {thread} thread";
                        _ranDry = true;
                    }
                }
            }
            Array.Clear(data, read, data.Length - read);
        }

        private void ReadSamples(Process process)
        {
            var errorReader = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null)
                        OnErrorLine(line);
                }
                catch (Exception) { /* the process was killed */ }
            }) { IsBackground = true };
            errorReader.Start();

            try
            {
                PlayFrom(process.StandardOutput.BaseStream);
                if (_stopping) return;

                process.WaitForExit();
                errorReader.Join(StopTimeoutMs);
                if (process.ExitCode != 0)
                    _error = Failure(process.ExitCode);
                _finished = true;
            }
            catch (Exception ex) when (!_stopping)
            {
                _error = ex.Message;
                _finished = true;
            }
            catch (Exception)
            {
                // Stop killed the process while it was being read.
            }
        }

        private void PlayFrom(Stream stdout)
        {
            var bytes = new byte[16384];
            var samples = new float[bytes.Length / sizeof(float)];
            int carried = 0;
            int read;
            // While draining, reading continues after Stop.
            while ((!_stopping || _draining) && (read = stdout.Read(bytes, carried, bytes.Length - carried)) > 0)
            {
                int available = carried + read;
                int count = available / sizeof(float);
                Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
                Shape(samples, count);
                // A read can end inside a sample.
                carried = available - count * sizeof(float);
                Buffer.BlockCopy(bytes, count * sizeof(float), bytes, 0, carried);
                if (!_draining)
                    Tap?.Invoke(samples, count);

                int queued = 0;
                while (!_stopping && !_draining && queued < count)
                {
                    queued += _fanOut.Write(samples, queued, count - queued);
                    if (queued < count)
                        Thread.Sleep(10);
                }
            }
        }

        private class Output
        {
            public AudioSource Source;
            public AudioClip Clip;
            public FloatRingBuffer Queue;
        }
    }
}
