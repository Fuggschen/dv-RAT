using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Plays a program's sound through the tablet's sources via ratcapture.exe (<see cref="CaptureProtocol"/>), or
    /// ratcapture-linux under Wine (<see cref="LinuxBridge"/>). The only class that starts either helper.
    /// </summary>
    public class CaptureTrack : PcmProcessTrack
    {
        // The helper restores the program's volume before it ends, and Stop waits this long for it.
        private const int RestoreTimeoutMs = 4500;

        private readonly object _stdinLock = new object();
        private volatile bool _waiting = true;
        private volatile bool _hasMedia;
        private volatile string _mediaStatus;
        private volatile string _title;
        private volatile string _problem;

        // Under Wine: the helper connection, and commands queued until it connects.
        private readonly bool _linux = CaptureApps.UnderWine;
        private TcpListener _listener;
        private Socket _socket;
        private readonly List<string> _unsent = new List<string>();

        public static string ExePath =>
            RATMain.ModEntry != null ? Path.Combine(RATMain.ModEntry.Path, "capture", "ratcapture.exe") : null;

        /// <summary>True when this platform's helper exists.</summary>
        public static bool Available => CaptureApps.UnderWine ? LinuxBridge.Available : ExePath != null && File.Exists(ExePath);

        private string LogPrefix => _linux ? "PC Audio (Linux): " : "PC Audio: ";

        /// <summary>True: <see cref="PcmProcessTrack.Stop"/> waits for the helper to restore the program, for game quit.</summary>
        public bool WaitOnStop { get; set; }

        protected override int EndsByItselfWithinMs => WaitOnStop ? 0 : RestoreTimeoutMs;

        /// <summary>True while the program is not running.</summary>
        public bool Waiting => _waiting;

        /// <summary>True when Windows' media controls know the program.</summary>
        public bool HasMedia => _hasMedia;

        /// <summary>E.g. "Playing" or "Paused"; null without media controls.</summary>
        public string MediaStatus => _mediaStatus;

        /// <summary>"Artist - Title", or the title alone; null without one.</summary>
        public string Title => _title;

        public void Start(string processName, string displayName, IEnumerable<AudioSource> sources)
        {
            if (!_linux)
            {
                Launch(ExePath, $"run \"{processName}\" {AudioSettings.outputSampleRate}", displayName, sources,
                    redirectInput: true, errorEncoding: new UTF8Encoding(false));
                return;
            }
            _listener = LinuxBridge.Start("run", processName, AudioSettings.outputSampleRate.ToString());
            RATMain.Log(LogPrefix + "started the helper for " + processName);
            // The reader thread waits for the connection.
            LaunchStream(() =>
            {
                var socket = LinuxBridge.Accept(_listener);
                lock (_stdinLock)
                {
                    _socket = socket;
                    foreach (string command in _unsent)
                        SendLine(command);
                    _unsent.Clear();
                }
                return new FrameReader(new NetworkStream(socket, false), OnErrorLine);
            }, displayName, sources);
        }

        // With _stdinLock held.
        private void SendLine(string command)
        {
            try
            {
                _socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
            }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
            {
                // The reader reports the end.
            }
        }

        /// <summary>Sends a <see cref="CaptureProtocol"/> command.</summary>
        public void Send(string command)
        {
            lock (_stdinLock)
            {
                if (_linux)
                {
                    if (_socket != null) SendLine(command);
                    else _unsent.Add(command);
                    return;
                }
                try
                {
                    if (Process == null || Process.HasExited) return;
                    Process.StandardInput.WriteLine(command);
                    Process.StandardInput.Flush();
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
                {
                    // Its exit is reported through Error.
                }
            }
        }

        protected override void OnErrorLine(string line)
        {
            var e = CaptureProtocol.ParseEvent(line);
            if (e == null) return;
            switch (e.Kind)
            {
                case CaptureEventKind.Waiting:
                    _waiting = true;
                    _hasMedia = false;
                    _mediaStatus = null;
                    _title = null;
                    break;
                case CaptureEventKind.Capturing:
                    _waiting = false;
                    break;
                case CaptureEventKind.Media:
                    _hasMedia = true;
                    _mediaStatus = e.Status;
                    _title = e.Artist.Length > 0 && e.Title.Length > 0 ? $"{e.Artist} - {e.Title}"
                        : e.Title.Length > 0 ? e.Title : null;
                    break;
                case CaptureEventKind.NoMedia:
                    _hasMedia = false;
                    _mediaStatus = null;
                    _title = null;
                    break;
                case CaptureEventKind.Error:
                    _problem = e.Message;
                    break;
                case CaptureEventKind.Log:
                    RATMain.Log(LogPrefix + e.Message);
                    break;
            }
        }

        // Read per buffer, so a slider change is heard while the program plays.
        protected override void Shape(float[] samples, int count) =>
            CaptureBoost.Apply(samples, count, CaptureBoost.Gain(RATMain.Settings?.PcAudioBoostDb ?? CaptureBoost.DefaultDb));

        protected override string Failure(int exitCode) => _problem ?? $"ratcapture ended with code {exitCode}";

        protected override string StreamEnded() => _problem ?? "the Linux helper ended";

        // Closing stdin ends the helper, which first gives the program its volume back; killing it would not.
        protected override void EndGracefully()
        {
            DropSamples();
            if (_linux)
            {
                // The helper restores the program by itself on disconnect, and nothing waits for it.
                lock (_stdinLock)
                {
                    _listener?.Stop();
                    try
                    {
                        _socket?.Shutdown(SocketShutdown.Both);
                    }
                    catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException)
                    {
                        // Gone already.
                    }
                    _socket?.Close();
                }
                return;
            }
            lock (_stdinLock)
            {
                try
                {
                    if (Process == null || Process.HasExited) return;
                    Process.StandardInput.Close();
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidOperationException)
                {
                    return;
                }
            }
            if (WaitOnStop && !Process.WaitForExit(RestoreTimeoutMs))
                RATMain.LogWarning("PC Audio: ratcapture did not end in time; double-click capture/ratcapture.exe if a program stays silent");
        }
    }
}
