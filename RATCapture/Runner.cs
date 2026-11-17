using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using DVRAT;

namespace RatCapture
{
    /// <summary>
    /// <c>ratcapture run</c>: raw 32-bit float stereo to stdout, status lines (see <see cref="CaptureProtocol"/>) to stderr,
    /// commands on stdin. Runs until stdin closes and follows the program through restarts.
    /// </summary>
    public sealed class Runner
    {
        private const int HousekeepingMs = 2000;
        private const int WaitPollMs = 500;
        // Sound just before a level change still has the old level, so it is dropped, not boosted.
        private const int LevelChangeMuteMs = 150;
        // A sample this loud after boosting is from a stream not turned down yet.
        private const float UnquietSample = 1.5f;
        // Rate limit for lost-sound reports.
        private const int LostSoundReportMs = 10000;
        // A program reports "paused" before its last sound is out and some fade out, so volume returns only this long after.
        private const int PausedSettleMs = 500;
        // A program that keeps playing past this stays silent on the PC.
        private const int PauseTimeoutMs = 3000;
        private const int PausePollMs = 100;

        private readonly string _process;
        private readonly int _sampleRate;
        private readonly string _restoreFile;
        private readonly TextWriter _status;
        private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
        private readonly Quieter _quieter;
        private readonly MediaWatcher _media;
        private volatile bool _inputEnded;
        private bool _wantQuiet;
        // "quiet off" while still playing: levels go back once paused, see PausedSettleMs.
        private bool _quietOffPending;
        private long _quietOffDeadline;
        private long _pausedSince = -1;
        private long _nextPauseCheck;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public Runner(string process, int sampleRate, string restoreFile, TextWriter status)
        {
            _process = process;
            _sampleRate = sampleRate;
            _restoreFile = restoreFile;
            _status = status;
            _quieter = new Quieter(restoreFile);
            _media = new MediaWatcher(process, status);
        }

        public void Run()
        {
            new Thread(ReadCommands) { IsBackground = true, Name = "commands" }.Start();
            _media.Start();
            int restored = Quieter.Restore(_restoreFile);
            if (restored > 0)
                _status.WriteLine(CaptureProtocol.LogLine($"Put back the levels of {restored} sound session(s) left down before"));

            using (var stdout = Console.OpenStandardOutput())
            {
                try
                {
                    bool waiting = false;
                    while (!_inputEnded)
                    {
                        int root = ProcessTree.Snapshot().RootOf(_process);
                        if (root == 0)
                        {
                            if (!waiting) _status.WriteLine(CaptureProtocol.WaitingLine());
                            waiting = true;
                            HandleCommands(null, 0);
                            Thread.Sleep(WaitPollMs);
                            continue;
                        }
                        waiting = false;
                        Capture(root, stdout);
                    }
                }
                catch (IOException)
                {
                    // The mod and its stdout are gone.
                }
                finally
                {
                    // Pause first, so the program does not go on loud on the PC.
                    PauseAndWait();
                    _quieter.TurnOff();
                }
            }
        }

        private void Capture(int root, Stream stdout)
        {
            // A restarted program may come back with levels a dead run left down.
            Quieter.Restore(_restoreFile);
            var tree = ProcessTree.Snapshot();
            using (var loopback = new ProcessLoopback(root, _sampleRate))
            {
                loopback.Start();
                _status.WriteLine(CaptureProtocol.CapturingLine());
                _media.Resend();
                if (_wantQuiet) _quieter.TurnOn(tree, root);

                var buffer = new float[_sampleRate * ProcessLoopback.Channels / 10];
                var bytes = new byte[buffer.Length * sizeof(float)];
                var clock = Stopwatch.StartNew();
                long nextHousekeeping = HousekeepingMs, mutedUntil = LevelChangeMuteMs, nextLostReport = 0;
                int reportedLost = 0;

                while (!_inputEnded)
                {
                    if (HandleCommands(tree, root))
                        mutedUntil = clock.ElapsedMilliseconds + LevelChangeMuteMs;

                    int count = loopback.Read(buffer, 20);
                    float gain = _quieter.On ? 1f / Quieter.Level : 1f;
                    bool tooLoud = false;
                    if (clock.ElapsedMilliseconds < mutedUntil)
                        Array.Clear(buffer, 0, count);
                    else if (gain != 1f)
                        for (int i = 0; i < count; i++)
                        {
                            float sample = buffer[i] * gain;
                            tooLoud |= Math.Abs(sample) > UnquietSample;
                            buffer[i] = Math.Max(-1f, Math.Min(1f, sample));
                        }
                    if (tooLoud)
                    {
                        Array.Clear(buffer, 0, count);
                        nextHousekeeping = 0;
                        mutedUntil = clock.ElapsedMilliseconds + LevelChangeMuteMs;
                    }
                    Buffer.BlockCopy(buffer, 0, bytes, 0, count * sizeof(float));
                    stdout.Write(bytes, 0, count * sizeof(float));
                    stdout.Flush();

                    long now = clock.ElapsedMilliseconds;
                    if (_quietOffPending && QuietOffWhenPaused())
                        mutedUntil = now + LevelChangeMuteMs;
                    if (loopback.Discontinuities > reportedLost && now >= nextLostReport)
                    {
                        _status.WriteLine(CaptureProtocol.LogLine(
                            $"Windows dropped sound {loopback.Discontinuities - reportedLost} time(s): the game read too slowly"));
                        reportedLost = loopback.Discontinuities;
                        nextLostReport = now + LostSoundReportMs;
                    }
                    if (now >= nextHousekeeping)
                    {
                        nextHousekeeping = now + HousekeepingMs;
                        tree = ProcessTree.Snapshot();
                        if (!string.Equals(tree.Name(root), _process, StringComparison.OrdinalIgnoreCase))
                        {
                            // Ended: the restore file keeps what could not be put back.
                            _status.WriteLine(CaptureProtocol.LogLine(_process + " ended"));
                            bool quiet = _wantQuiet;
                            _quieter.TurnOff();
                            _wantQuiet = quiet;
                            return;
                        }
                        _quieter.Update(tree, root);
                    }
                }
            }
        }

        /// <summary>True when the levels changed.</summary>
        private bool HandleCommands(ProcessTree tree, int root)
        {
            bool levelsChanged = false;
            while (_commands.TryDequeue(out string command))
            {
                switch (command)
                {
                    case CaptureProtocol.QuietOn:
                        _wantQuiet = true;
                        _quietOffPending = false;
                        if (tree != null && !_quieter.On)
                        {
                            _quieter.TurnOn(tree, root);
                            levelsChanged = true;
                        }
                        break;
                    case CaptureProtocol.QuietOff:
                        _wantQuiet = false;
                        if (!_quieter.On) break;
                        if (MediaStatus() == "Playing")
                        {
                            // Usually right after "pause", before the program has stopped.
                            _quietOffPending = true;
                            _quietOffDeadline = _clock.ElapsedMilliseconds + PauseTimeoutMs;
                            _pausedSince = -1;
                            break;
                        }
                        _quieter.TurnOff();
                        levelsChanged = true;
                        break;
                    case CaptureProtocol.Play:
                    case CaptureProtocol.Pause:
                    case CaptureProtocol.Next:
                    case CaptureProtocol.Previous:
                        _media.Press(command);
                        break;
                    default:
                        _status.WriteLine(CaptureProtocol.LogLine($"Unknown command '{command}'"));
                        break;
                }
            }
            return levelsChanged;
        }

        /// <summary>True when the volume was given back.</summary>
        private bool QuietOffWhenPaused()
        {
            long now = _clock.ElapsedMilliseconds;
            if (now < _nextPauseCheck) return false;
            _nextPauseCheck = now + PausePollMs;
            if (MediaStatus() == "Playing")
            {
                _pausedSince = -1;
                if (now < _quietOffDeadline) return false;
                _quietOffPending = false;
                _status.WriteLine(CaptureProtocol.LogLine($"{_process} kept playing; it stays silent on the PC"));
                return false;
            }
            if (_pausedSince < 0) _pausedSince = now;
            if (now - _pausedSince < PausedSettleMs) return false;
            _quietOffPending = false;
            _quieter.TurnOff();
            return true;
        }

        /// <summary>Returns at once without media controls.</summary>
        private void PauseAndWait()
        {
            if (MediaStatus() != "Playing") return;
            _media.Press(CaptureProtocol.Pause);
            var waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < PauseTimeoutMs && MediaStatus() == "Playing")
                Thread.Sleep(PausePollMs);
            if (_quieter.On)
                Thread.Sleep(PausedSettleMs);
        }

        /// <summary>Null without media controls.</summary>
        private string MediaStatus() => _media.Status;

        private void ReadCommands()
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                    _commands.Enqueue(line.Trim());
            }
            catch (IOException)
            {
                // The mod is gone.
            }
            _inputEnded = true;
        }
    }
}
