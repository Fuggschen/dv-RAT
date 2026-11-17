using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using DVRAT;

namespace RatCapture
{
    /// <summary>Runs on its own thread, as a media session can stop answering and on the recording thread that stopped the sound.</summary>
    public sealed class MediaWatcher
    {
        private const int PollMs = 250;

        private readonly string _process;
        private readonly TextWriter _status;
        private readonly ConcurrentQueue<string> _keys = new ConcurrentQueue<string>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private volatile bool _resend;
        private volatile string _playbackStatus;

        public MediaWatcher(string process, TextWriter status)
        {
            _process = process;
            _status = status;
        }

        /// <summary>Last read status, null without a media session. Kept while the controls hang.</summary>
        public string Status => _playbackStatus;

        public void Start() => new Thread(Run) { IsBackground = true, Name = "media" }.Start();

        /// <summary>Takes one of the <see cref="CaptureProtocol"/> keys.</summary>
        public void Press(string key)
        {
            _keys.Enqueue(key);
            _wake.Set();
        }

        /// <summary>Reports the song at the next read even if unchanged.</summary>
        public void Resend()
        {
            _resend = true;
            _wake.Set();
        }

        private void Run()
        {
            MediaControls media;
            try
            {
                media = new MediaControls();
            }
            catch (Exception ex)
            {
                // Sound still plays, without title and keys.
                _status.WriteLine(CaptureProtocol.LogLine("No media controls: " + ex.Message));
                return;
            }

            string last = null, lastError = null;
            bool stuck = false;
            while (true)
            {
                string line = null;
                try
                {
                    if (media == null) media = new MediaControls();
                    while (_keys.TryDequeue(out string key))
                        if (!media.Send(_process, key))
                            _status.WriteLine(CaptureProtocol.LogLine($"{_process} did not take '{key}'"));
                    var now = media.Now(_process);
                    _playbackStatus = now?.Status;
                    line = now.HasValue
                        ? CaptureProtocol.MediaLine(now.Value.Status, now.Value.Artist, now.Value.Title)
                        : CaptureProtocol.NoMediaLine();
                    if (stuck)
                        _status.WriteLine(CaptureProtocol.LogLine("Windows' media controls answer again"));
                    stuck = false;
                    lastError = null;
                }
                catch (TimeoutException)
                {
                    if (!stuck)
                        _status.WriteLine(CaptureProtocol.LogLine(
                            $"Windows' media controls did not answer within {MediaControls.TimeoutMs / 1000} s; asking them again"));
                    stuck = true;
                    // The hung manager may stay hung, so the next round makes a new one.
                    media = null;
                }
                catch (Exception ex)
                {
                    // The player closed between finding its session and reading it.
                    _playbackStatus = null;
                    line = CaptureProtocol.NoMediaLine();
                    if (ex.Message != lastError)
                        _status.WriteLine(CaptureProtocol.LogLine("Media controls: " + ex.Message));
                    lastError = ex.Message;
                }

                if (line != null && (line != last || _resend))
                {
                    _resend = false;
                    last = line;
                    _status.WriteLine(line);
                }
                _wake.WaitOne(PollMs);
            }
        }
    }
}
