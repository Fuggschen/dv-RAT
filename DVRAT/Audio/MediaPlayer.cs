using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DV;
using DV.Radio;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    public enum PlayState { Stopped, Playing, Paused }

    /// <summary>
    /// Plays a folder's music, radio or PC Audio through the tablet. Songs decode via <see cref="FfmpegTrack"/>, with the
    /// game's <see cref="FilePlayer"/> (used only here) as mp3, ogg and wav fallback. Pages read state here, no events.
    /// </summary>
    public class MediaPlayer : MonoBehaviour
    {
        /// <summary>FilePlayer throws for any other extension.</summary>
        public static readonly string[] GameExtensions = { ".mp3", ".ogg", ".wav" };

        /// <summary>Previous restarts the song after this much has played, else goes to the song before.</summary>
        public const float RestartAfterSeconds = 3f;

        /// <summary>
        /// Priority of the music sources (0 is highest, Unity's default 128). Unity stops the lowest first, which
        /// would stall the song.
        /// </summary>
        public const int MusicPriority = 16;

        private AudioSource _source;
        private FilePlayer _player;
        private FfmpegTrack _ffmpeg;
        private RadioStream _radio;
        // Set in the download's callback, reported by Update.
        private string _radioStartError;
        // Kept so Play after Stop tunes in again. Null while songs play.
        private RadioStation _station;
        private CaptureApp _captureApp;
        private CaptureTrack _capture;
        private PlayQueue _queue;
        private readonly System.Random _random = new System.Random();
        // Handled in Update so the next song never starts inside the old player's coroutine.
        private bool _trackEnded;
        private bool _pausedByGame;
        // The FilePlayer's clip has no values while paused, so they are kept here.
        private int _clipFrequency;
        private float _clipLength;

        private readonly object _scanLock = new object();
        private MusicFolderTree _scanResult;
        private volatile bool _scanning;

        /// <summary>Null until the first scan has finished.</summary>
        public MusicFolderTree Library { get; private set; }

        public bool Scanning => _scanning;

        /// <summary>True when the last scan found ffmpeg.exe and listed its extra formats.</summary>
        public bool ExtraFormats { get; private set; }

        // This copy's own backend state. While another player's copy is the source, the public properties
        // answer from _remote instead and no backend starts here.
        private PlayState _state = PlayState.Stopped;
        private RemoteMedia _remote;

        public PlayState State => _remote != null ? _remote.State : _state;

        /// <summary>True while another player's copy plays and this one shows it.</summary>
        public bool IsRemote => _remote != null;

        public string RemoteSource => _remote?.Source;

        /// <summary>Null when stopped.</summary>
        public string CurrentFile => _remote != null ? _remote.CurrentFile : _state != PlayState.Stopped && _station == null && _captureApp == null ? _queue?.Current : null;

        /// <summary>Null when stopped or when a song plays.</summary>
        public RadioStation Station => _remote != null ? _remote.Station : _state != PlayState.Stopped ? _station : null;

        /// <summary>Also set after Stop; null once a song was played.</summary>
        public RadioStation LastStation => _remote != null ? _remote.LastStation : _station;

        /// <summary>Null until the station sends one.</summary>
        public string RadioTitle => _remote != null ? _remote.RadioTitle : _radio?.Title;

        /// <summary>True from choosing a station until its first audio arrives.</summary>
        public bool RadioConnecting => _remote != null ? _remote.RadioConnecting : _radio != null && !_radio.Receiving;

        /// <summary>True while the buffer fills, at one signal bar or less.</summary>
        public bool RadioBuffering => _remote != null ? _remote.RadioBuffering : _radio != null && _ffmpeg != null && _ffmpeg.Buffering;

        /// <summary>Null when stopped or when something else plays.</summary>
        public CaptureApp CaptureApp => _remote != null ? _remote.CaptureApp : _state != PlayState.Stopped ? _captureApp : null;

        /// <summary>Also set after Stop; null once a song or station was played.</summary>
        public CaptureApp LastCaptureApp => _remote != null ? _remote.LastCaptureApp : _captureApp;

        /// <summary>True while the program is not running.</summary>
        public bool CaptureWaiting => _remote != null ? _remote.CaptureWaiting : _capture != null && _capture.Waiting;

        /// <summary>"Artist - Title" from Windows' media controls; null when none.</summary>
        public string CaptureTitle => _remote != null ? _remote.CaptureTitle : _capture?.Title;

        /// <summary>E.g. "Playing" or "Paused"; null without media controls.</summary>
        public string CaptureStatus => _remote != null ? _remote.CaptureStatus : _capture?.MediaStatus;

        private bool CaptureKeys => _captureApp != null && _capture != null && _capture.HasMedia;
        private bool CaptureRunning => CaptureKeys && _capture.MediaStatus == "Playing";

        /// <summary>Null before anything played.</summary>
        public IReadOnlyList<string> QueueFiles => _queue?.Files;

        // A key that would do nothing is shown dimmed.
        public bool CanStop => _remote != null ? _remote.CanStop : _state != PlayState.Stopped;
        public bool CanPlay => _remote != null ? _remote.CanPlay : _captureApp != null
            ? _state == PlayState.Stopped || CaptureKeys && !CaptureRunning
            : _state == PlayState.Paused || _state == PlayState.Stopped && (_queue != null || _station != null);
        // Live radio only stops. A program pauses only through its own media controls.
        public bool CanPause => _remote != null ? _remote.CanPause : _captureApp != null
            ? _state == PlayState.Playing && CaptureRunning
            : _state == PlayState.Playing && _station == null;
        public bool CanNext => _remote != null ? _remote.CanNext : _captureApp != null
            ? _state != PlayState.Stopped && CaptureKeys
            : _station == null && _queue != null && _queue.HasNext;
        public bool CanPrevious => _remote != null ? _remote.CanPrevious : _captureApp != null
            ? _state != PlayState.Stopped && CaptureKeys
            : _station == null && _queue != null;

        public const string VolumeSetting = "mediaVolume";
        public const int VolumeStep = 10;
        private RATTabletController _tablet;
        private string _appliedVolume;

        /// <summary>Percent, 0 to 100. Saved with the tablet's settings.</summary>
        public int Volume
        {
            get
            {
                string text = _tablet != null ? _tablet.Session.Setting(VolumeSetting) : null;
                return int.TryParse(text, out int percent) ? Mathf.Clamp(percent, 0, 100) : 100;
            }
        }

        public void ChangeVolume(int delta)
        {
            if (_tablet == null) return;
            _tablet.Session.Settings[VolumeSetting] = Mathf.Clamp(Volume + delta, 0, 100).ToString();
        }

        /// <summary>Carries over to the next queue.</summary>
        public bool Shuffle
        {
            get => _remote != null ? _remote.Shuffle : _shuffle;
            private set => _shuffle = value;
        }

        private bool _shuffle;

        // Speakers and subwoofer on the same train. The tablet's own speaker is quiet while there are any.
        private const float DeviceCheckSeconds = 2f;
        private List<RATSpeaker> _devices = new List<RATSpeaker>();
        private float _nextDeviceCheck;

        /// <summary>Speakers first, in the order found.</summary>
        public IReadOnlyList<RATSpeaker> Devices => _devices;

        /// <summary>Null after a song started.</summary>
        public string Error
        {
            get => _remote != null ? _remote.Error : _error;
            private set => _error = value;
        }

        private string _error;

        /// <summary>From the mod settings, else the Windows Music folder.</summary>
        public static string Folder
        {
            get
            {
                string configured = RATMain.Settings?.MusicFolder?.Trim();
                return string.IsNullOrEmpty(configured)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                    : configured;
            }
        }

        private void Awake()
        {
            _tablet = GetComponent<RATTabletController>();
            var holder = new GameObject("MediaPlayer");
            holder.transform.SetParent(transform, false);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.priority = MusicPriority;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.dopplerLevel = 0f;
            ToneFilter.UseFalloff(_source, 0.5f, 25f);
            holder.AddComponent<ToneFilter>().Settings = ToneSettings.TabletSpeaker();
            holder.AddComponent<MuffleFilter>();
        }

        // The pause menu does not pause audio by itself.
        private void Start()
        {
            var app = AppUtil.Instance;
            if (app == null) return;
            app.GamePaused += OnGamePaused;
            app.GameUnpaused += OnGameUnpaused;
        }

        private void OnGamePaused()
        {
            if (_state != PlayState.Playing || MultiplayerSync.InGame) return;
            PauseBackend();
            _pausedByGame = true;
        }

        private void OnGameUnpaused()
        {
            if (!_pausedByGame) return;
            _pausedByGame = false;
            if (_station != null)
            {
                PlayRadio(_station);
                return;
            }
            ResumeBackend();
        }

        /// <summary>Scans on a worker thread. Ignored while a scan runs.</summary>
        public void Rescan()
        {
            if (_scanning) return;
            _scanning = true;
            string folder = Folder;
            var extensions = new List<string>(GameExtensions);
            ExtraFormats = FfmpegTrack.Available;
            if (ExtraFormats)
                extensions.AddRange(FfmpegTrack.Extensions);
            Task.Run(() =>
            {
                MusicFolderTree tree;
                try
                {
                    tree = new MusicFolderTree(folder, MusicLibrary.Scan(folder, extensions));
                }
                catch (Exception ex)
                {
                    RATMain.LogWarning($"Reading the music folder {folder} failed: {ex}");
                    tree = new MusicFolderTree(folder, new string[0]);
                }
                lock (_scanLock)
                    _scanResult = tree;
                _scanning = false;
            });
        }

        /// <summary>Plays one folder's songs from <paramref name="index"/> on.</summary>
        public void Play(IReadOnlyList<string> files, int index)
        {
            if (files == null || index < 0 || index >= files.Count) return;
            BecomeSource();
            _station = null;
            _captureApp = null;
            _queue = new PlayQueue(files, index, Shuffle, _random);
            StartCurrent();
        }

        /// <summary>Resumes a paused song, or restarts the last one after Stop.</summary>
        public void PlayOrResume()
        {
            if (Forward(MediaCommands.Play)) return;
            if (_captureApp != null)
            {
                if (_state == PlayState.Stopped)
                {
                    PlayCapture(_captureApp);
                    return;
                }
                // Silent on the PC again, then the program's own play key.
                _capture?.Send(CaptureProtocol.QuietOn);
                _capture?.Send(CaptureProtocol.Play);
                _state = PlayState.Playing;
            }
            else if (_state == PlayState.Paused)
            {
                ResumeBackend();
                _state = PlayState.Playing;
            }
            else if (_state == PlayState.Stopped && _station != null)
            {
                PlayRadio(_station);
            }
            else if (_state == PlayState.Stopped && _queue != null)
            {
                StartCurrent();
            }
        }

        public void Pause()
        {
            if (Forward(MediaCommands.Pause)) return;
            if (!CanPause) return;
            if (_captureApp != null)
            {
                // The program gets its PC volume back.
                _capture.Send(CaptureProtocol.Pause);
                _capture.Send(CaptureProtocol.QuietOff);
            }
            else
            {
                PauseBackend();
            }
            _state = PlayState.Paused;
        }

        public void Next()
        {
            if (Forward(MediaCommands.Next)) return;
            if (!CanNext) return;
            if (_captureApp != null)
                _capture.Send(CaptureProtocol.Next);
            else if (_queue.MoveNext())
                StartCurrent();
        }

        public void Previous()
        {
            if (Forward(MediaCommands.Previous)) return;
            if (!CanPrevious) return;
            if (_captureApp != null)
            {
                _capture.Send(CaptureProtocol.Previous);
                return;
            }
            if (_state == PlayState.Stopped || PlayedSeconds() <= RestartAfterSeconds)
                _queue.MovePrevious();
            StartCurrent();
        }

        public void SetShuffle(bool on)
        {
            if (Forward(on ? MediaCommands.ShuffleOn : MediaCommands.ShuffleOff)) return;
            Shuffle = on;
            if (_queue != null)
                _queue.Shuffle = on;
        }

        public void Stop()
        {
            if (Forward(MediaCommands.Stop)) return;
            LocalStop();
        }

        // Local only, never forwarded, e.g. when the tablet goes into the inventory.
        private void LocalStop()
        {
            StopBackend();
            _state = PlayState.Stopped;
        }

        /// <summary>Needs ffmpeg. It starts with the first audio, once the stream's format is known.</summary>
        public void PlayRadio(RadioStation station)
        {
            if (station == null) return;
            BecomeSource();
            StopBackend();
            _station = station;
            _captureApp = null;
            Error = null;
            _state = PlayState.Playing;
            if (!FfmpegTrack.Available)
            {
                OnErrorInfo("internet radio needs ffmpeg.exe in the mod's ffmpeg folder");
                return;
            }

            var tap = TapsForNewTrack();
            _radio = new RadioStream(contentType =>
            {
                _ffmpeg = new FfmpegTrack { Tap = tap };
                try
                {
                    // Same reception as the game's boombox; 1 with signal loss off.
                    var signal = _tablet != null ? _tablet.RadioSignal : null;
                    _ffmpeg.StartStream(station.Name, RadioPlaylist.FfmpegFormat(contentType), Sources(),
                        signal != null ? () => signal.Strength : (Func<float>)null);
                }
                catch (Exception ex)
                {
                    _ffmpeg = null;
                    _radioStartError = $"ffmpeg did not start: {ex.Message}";
                }
            }, (data, offset, count) => _ffmpeg?.WriteInput(data, offset, count));
            _radio.Start(station.Url);
            if (station is DirectoryStation listed)
                RadioDirectory.CountPlay(listed);
        }

        /// <summary>PC Audio: keeps the program silent on the PC meanwhile, and waits while it is not running.</summary>
        public void PlayCapture(CaptureApp app)
        {
            if (app == null) return;
            if (CaptureApps.IsThisGame(app.ProcessName))
            {
                RATMain.LogWarning($"PC Audio: {app.ProcessName} is the game itself; it cannot play through its own tablet");
                return;
            }
            BecomeSource();
            StopBackend();
            _station = null;
            _captureApp = app;
            Error = null;
            _state = PlayState.Playing;
            if (!CaptureTrack.Available)
            {
                OnErrorInfo("PC Audio needs ratcapture.exe in the mod's capture folder");
                return;
            }
            _capture = new CaptureTrack { Tap = TapsForNewTrack() };
            try
            {
                _capture.Start(app.ProcessName, app.DisplayName, Sources());
                _capture.Send(CaptureProtocol.QuietOn);
                // Stop paused the program. A playing one ignores this.
                _capture.Send(CaptureProtocol.Play);
            }
            catch (Exception ex)
            {
                OnErrorInfo($"ratcapture did not start: {ex.Message}");
            }
        }

        // The tablet's source comes first: it is the clock and keeps playing, muted, while speakers play.
        private List<AudioSource> Sources()
        {
            var sources = new List<AudioSource> { _source };
            foreach (var device in _devices)
                sources.Add(device.Source);
            return sources;
        }

        private void StartCurrent()
        {
            StopBackend();
            string path = _queue.Current;
            Error = null;
            _state = PlayState.Playing;

            // The game's player is only the mp3, ogg and wav fallback while ffmpeg.exe is missing.
            bool gameFormat = Array.IndexOf(GameExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
            if (FfmpegTrack.Available || !gameFormat)
            {
                _ffmpeg = new FfmpegTrack { Tap = TapsForNewTrack() };
                try
                {
                    _ffmpeg.Start(path, Sources());
                }
                catch (Exception ex)
                {
                    OnErrorInfo($"ffmpeg did not start: {ex.Message}");
                }
                return;
            }

            if (MultiplayerSync.InGame && !_toldNoStream)
            {
                _toldNoStream = true;
                RATMain.LogWarning("Multiplayer: songs play without ffmpeg.exe, so the other players do not hear them");
            }
            // A new player per file: a stopped FilePlayer's coroutine runs one more frame and could stop the next clip.
            _player = _source.gameObject.AddComponent<FilePlayer>();
            _player.audioSource = _source;
            // Otherwise a focus change would look like the end of the song.
            _player.StopOnFocusLost = false;
            _player.PlaybackStopped += OnPlaybackStopped;
            _player.ErrorInfo += OnErrorInfo;
            _player.SetFile(new Uri(path).AbsoluteUri, 0);
            _player.Play();
        }

        /// <summary>Seconds played; 0 when stopped.</summary>
        public float Position => _remote != null ? _remote.Position : _state != PlayState.Stopped ? PlayedSeconds() : 0f;

        /// <summary>Seconds; 0 while unknown or stopped.</summary>
        public float Duration =>
            _remote != null ? _remote.Duration : _state == PlayState.Stopped ? 0f
            : _ffmpeg != null ? _ffmpeg.DurationSeconds
            : _clipLength;

        private float PlayedSeconds()
        {
            if (_ffmpeg != null) return _ffmpeg.PlayedSeconds;
            // A paused FilePlayer drops its clip but keeps the position, in samples.
            return _player != null && _clipFrequency > 0 ? (float)_player.GetSeekPosition() / _clipFrequency : 0f;
        }

        private void PauseBackend()
        {
            _player?.Pause();
            _ffmpeg?.Pause();
            _capture?.Pause();
        }

        private void ResumeBackend()
        {
            _player?.Play();
            _ffmpeg?.Resume();
            _capture?.Resume();
        }

        private void StopBackend()
        {
            _trackEnded = false;
            _pausedByGame = false;
            _clipFrequency = 0;
            _clipLength = 0f;
            _radio?.Stop();
            _radio = null;
            _ffmpeg?.Stop();
            _ffmpeg = null;
            _capture?.Stop();
            _capture = null;
            if (_player == null) return;

            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.ErrorInfo -= OnErrorInfo;
            _player.Stop();
            Destroy(_player);
            _player = null;
        }

        // A paused FilePlayer reports a stop too.
        private void OnPlaybackStopped()
        {
            if (_state == PlayState.Playing && !_pausedByGame)
                _trackEnded = true;
        }

        private void OnErrorInfo(string error)
        {
            string name = _captureApp != null ? _captureApp.DisplayName
                : _station != null ? _station.Name : Path.GetFileName(_queue?.Current);
            RATMain.LogWarning($"Media player: {name}: {error}");
            Error = $"{name}: {error}";
            LocalStop();
        }

        #region Spectrum

        // Each track starts a new history.
        private SampleHistory _history;

        private Action<float[], int> HistoryTap()
        {
            // The track reads up to 2 s of queue plus about 0.5 s of Unity read-ahead ahead of the speakers.
            var history = new SampleHistory(AudioSettings.outputSampleRate * 4);
            _history = history;
            return (samples, count) => history.Write(samples, count, 2);
        }

        private PcmProcessTrack PlayingTrack => _ffmpeg ?? (PcmProcessTrack)_capture ?? _network;

        /// <summary>Fills the window with the latest played samples as mono. False while no track plays, e.g. the game's player.</summary>
        public bool SpectrumWindow(float[] window)
        {
            var track = PlayingTrack;
            var history = _history;
            return track != null && history != null && State == PlayState.Playing && history.Read(track.PlayedFrames, window);
        }

        #endregion

        #region Multiplayer

        // As the source: each start is a new epoch.
        private IAudioEncoder _encoder;
        private uint _epoch;
        private bool _toldNoStream;
        // As a copy: the source's sound, reordered and played.
        private JitterBuffer _jitter;
        private NetworkTrack _network;
        private bool _networkPaused;

        /// <summary>Spectrum history, plus the other copies in a multiplayer game.</summary>
        private Action<float[], int> TapsForNewTrack()
        {
            var history = HistoryTap();
            var send = SendTap();
            if (send == null) return history;
            return (samples, count) =>
            {
                history(samples, count);
                send(samples, count);
            };
        }

        /// <summary>Null outside a game.</summary>
        private Action<float[], int> SendTap()
        {
            if (!MultiplayerSync.InGame || _tablet == null) return null;
            if (_encoder == null)
                _encoder = MultiplayerSync.CreateEncoder(_tablet.Session.Id);
            if (_encoder == null) return null;
            var encoder = _encoder;
            uint epoch = ++_epoch;
            return (samples, count) => encoder.Write(epoch, samples, count);
        }

        /// <summary>Dropped while this copy plays itself.</summary>
        public void ReceiveAudio(uint epoch, uint seq, byte[] packet)
        {
            if (_remote != null) _jitter?.Push(epoch, seq, packet);
        }

        // A fresh track per epoch, so the last song's tail never plays first.
        private void UpdateRemoteAudio()
        {
            if (_remote == null || _remote.State == PlayState.Stopped || _jitter == null)
            {
                StopNetwork();
                return;
            }
            if (_network != null && _network.Epoch != _jitter.Epoch)
                StopNetwork();
            if (_network == null)
            {
                if (_jitter.Epoch == 0) return;
                // One decoder per track: the old reader may still be on its last packet.
                var decoder = MultiplayerSync.CreateDecoder();
                if (decoder == null) return;
                _network = new NetworkTrack(_jitter, decoder) { Tap = HistoryTap() };
                _network.Start("R.A.T. " + _remote.Source, Sources());
                _networkPaused = false;
            }
            bool paused = _remote.State == PlayState.Paused;
            if (paused == _networkPaused) return;
            _networkPaused = paused;
            if (paused) _network.Pause();
            else _network.Resume();
        }

        private void StopNetwork()
        {
            _network?.Stop();
            _network = null;
        }

        // The state as last sent, without the position.
        private string _sentKey;
        private JObject _sent;

        /// <summary>For the other copies: own state as source, else the source's. Null while nothing has played here.</summary>
        public JObject SyncState()
        {
            if (_remote != null) return _remote.Json;
            string source = MultiplayerSync.LocalName;
            if (source == null || _queue == null && _station == null && _captureApp == null) return null;
            var json = new JObject
            {
                [RemoteMedia.SourceKey] = source,
                [RemoteMedia.StateKey] = _state.ToString(),
                [RemoteMedia.FileKey] = CurrentFile,
                [RemoteMedia.RadioTitleKey] = RadioTitle,
                [RemoteMedia.StationNameKey] = _station?.Name,
                [RemoteMedia.StationUrlKey] = _station?.Url,
                [RemoteMedia.CaptureProcessKey] = _captureApp?.ProcessName,
                [RemoteMedia.CaptureDisplayKey] = _captureApp?.DisplayName,
                [RemoteMedia.CaptureMediaKey] = CaptureKeys,
                [RemoteMedia.CaptureTitleKey] = CaptureTitle,
                [RemoteMedia.CaptureStatusKey] = CaptureStatus,
                [RemoteMedia.WaitingKey] = CaptureWaiting,
                [RemoteMedia.ConnectingKey] = RadioConnecting,
                [RemoteMedia.BufferingKey] = RadioBuffering,
                [RemoteMedia.DurationKey] = Mathf.Round(Duration),
                [RemoteMedia.ShuffleKey] = _shuffle,
                [RemoteMedia.ErrorKey] = _error,
                [RemoteMedia.CanPlayKey] = CanPlay,
                [RemoteMedia.CanPauseKey] = CanPause,
                [RemoteMedia.CanNextKey] = CanNext,
                [RemoteMedia.CanPreviousKey] = CanPrevious,
            };
            // The position goes along only when something else changed. The others count on from it.
            string key = json.ToString(Newtonsoft.Json.Formatting.None);
            if (key != _sentKey)
            {
                _sentKey = key;
                json[RemoteMedia.PositionKey] = Mathf.Round(Position * 10f) / 10f;
                _sent = json;
            }
            return _sent;
        }

        /// <summary>Applies another copy's <see cref="SyncState"/>. A state from another source ends local playback.</summary>
        public void ApplySync(JObject media)
        {
            if (media == null) return;
            string source = media.Value<string>(RemoteMedia.SourceKey);
            if (source == MultiplayerSync.LocalName)
            {
                BecomeSource();
                return;
            }
            if (_remote == null)
                LocalStop();
            // Another source counts its epochs from the start.
            if (_remote == null || _remote.Source != source)
            {
                _jitter = new JitterBuffer();
                StopNetwork();
            }
            if (_remote == null || !JToken.DeepEquals(_remote.Json, media))
                _remote = new RemoteMedia(media, Time.time);
        }

        /// <summary>The source player left, so the tablet stops for everyone.</summary>
        public void SourceLeft(string player)
        {
            if (_remote == null || _remote.Source != player) return;
            // An explicit stopped state: a missing one would leave the others showing it playing.
            _remote = new RemoteMedia(new JObject
            {
                [RemoteMedia.SourceKey] = "",
                [RemoteMedia.StateKey] = PlayState.Stopped.ToString(),
            }, Time.time);
        }

        /// <summary>Runs a key pressed on another copy.</summary>
        public void RunCommand(string command)
        {
            if (_remote != null) return;
            switch (command)
            {
                case MediaCommands.Play: PlayOrResume(); break;
                case MediaCommands.Pause: Pause(); break;
                case MediaCommands.Next: Next(); break;
                case MediaCommands.Previous: Previous(); break;
                case MediaCommands.Stop: Stop(); break;
                case MediaCommands.ShuffleOn: SetShuffle(true); break;
                case MediaCommands.ShuffleOff: SetShuffle(false); break;
            }
        }

        // The others stop their own playback when they hear of it.
        private void BecomeSource()
        {
            _remote = null;
            _jitter = null;
            StopNetwork();
        }

        // A key on a showing copy goes to the source.
        private bool Forward(string command)
        {
            // No source left: keys act as on a stopped tablet.
            if (_remote == null || string.IsNullOrEmpty(_remote.Source)) return false;
            if (_tablet != null)
                MultiplayerSync.SendMediaCommand(_tablet.Session.Id, _remote.Source, command);
            return true;
        }

        #endregion

        private void Update()
        {
            if (_remote != null && !MultiplayerSync.InGame)
                BecomeSource();
            UpdateRemoteAudio();

            MusicFolderTree scanned;
            lock (_scanLock)
            {
                scanned = _scanResult;
                _scanResult = null;
            }
            if (scanned != null)
                Library = scanned;

            // A tablet switched off drops its devices at once.
            bool ready = _tablet != null && _tablet.IsReady;
            if (Time.unscaledTime >= _nextDeviceCheck || !ready && _devices.Count > 0)
            {
                _nextDeviceCheck = Time.unscaledTime + DeviceCheckSeconds;
                UpdateDevices(ready);
            }
            _ffmpeg?.RemoveDestroyedOutputs();
            _capture?.RemoveDestroyedOutputs();
            _network?.RemoveDestroyedOutputs();
            _source.mute = _devices.Count > 0;
            foreach (var device in _devices)
                device.MasterVolume = _source.volume;

            // The setting can change from the page or a loaded save.
            string volume = _tablet != null ? _tablet.Session.Setting(VolumeSetting) : null;
            if (volume != _appliedVolume)
            {
                _appliedVolume = volume;
                _source.volume = Volume / 100f;
            }

            if (_player != null && _source.clip != null)
            {
                _clipFrequency = _source.clip.frequency;
                _clipLength = _source.clip.length;
            }

            if (_radioStartError != null)
            {
                string error = _radioStartError;
                _radioStartError = null;
                OnErrorInfo(error);
            }

            if (_radio != null)
            {
                _radio.Update();
                if (_radio.Error != null)
                    OnErrorInfo(_radio.Error);
                else if (_radio.Ended)
                    _ffmpeg?.EndInput();
            }

            if (_ffmpeg != null)
            {
                if (_ffmpeg.Error != null)
                    OnErrorInfo(_ffmpeg.Error);
                else if (_ffmpeg.Ended && _station != null)
                    OnErrorInfo("the station stopped sending");
                else if (_ffmpeg.Ended)
                    _trackEnded = true;
            }

            if (_capture != null)
            {
                // Resumed in the program or by a media key: play along and silence the PC again.
                if (_state == PlayState.Paused && !_pausedByGame && _capture.MediaStatus == "Playing")
                {
                    _capture.Send(CaptureProtocol.QuietOn);
                    _state = PlayState.Playing;
                }
                if (_capture.Error != null)
                    OnErrorInfo(_capture.Error);
                else if (_capture.Ended)
                    OnErrorInfo("the PC Audio helper stopped");
            }

            if (_trackEnded)
            {
                _trackEnded = false;
                if (_queue != null && _queue.MoveNext())
                    StartCurrent();
                else
                    LocalStop();
            }
        }

        // A deactivated FilePlayer stops without PlaybackStopped, e.g. in the inventory.
        private void OnDisable()
        {
            LocalStop();
            StopNetwork();
            UpdateDevices(false);
        }

        /// <summary>Connects free speakers on the train and drops those that left. Needs a track: FilePlayer feeds only the tablet's source.</summary>
        private void UpdateDevices(bool ready)
        {
            var car = ready && (FfmpegTrack.Available || _capture != null || _network != null) ? _tablet.TrainCar : null;
            var cars = car != null && car.trainset != null ? car.trainset.cars : null;
            var picked = new List<RATSpeaker>();
            if (cars != null)
            {
                var index = new Dictionary<TrainCar, int>();
                for (int i = 0; i < cars.Count; i++)
                    index[cars[i]] = i;
                // Connected devices first, then by place in the train.
                var candidates = new List<RATSpeaker>(_devices);
                var found = new List<RATSpeaker>(RATSpeaker.All);
                found.Sort((a, b) => Place(a, index).CompareTo(Place(b, index)));
                candidates.AddRange(found);
                picked = DevicePicker.Pick(candidates, d => d.Kind,
                    d => d != null && d.isActiveAndEnabled && (d.Owner == null || d.Owner == this)
                         && d.TrainCar != null && index.ContainsKey(d.TrainCar));
            }

            foreach (var device in _devices)
            {
                // A kept device switched off and on lost its owner and is reconnected below.
                if (picked.Contains(device) && device.Owner == this) continue;
                if (device != null)
                {
                    _ffmpeg?.RemoveOutput(device.Source);
                    _capture?.RemoveOutput(device.Source);
                    _network?.RemoveOutput(device.Source);
                    device.Connected = false;
                    device.Owner = null;
                }
            }
            foreach (var device in picked)
            {
                if (_devices.Contains(device) && device.Owner == this) continue;
                device.Owner = this;
                device.Connected = true;
                _ffmpeg?.AddOutput(device.Source);
                _capture?.AddOutput(device.Source);
                _network?.AddOutput(device.Source);
            }
            _devices = picked;
        }

        private static int Place(RATSpeaker device, Dictionary<TrainCar, int> index)
        {
            var car = device != null ? device.TrainCar : null;
            return car != null && index.TryGetValue(car, out int place) ? place : int.MaxValue;
        }

        // OnDisable may not run when the game exits; ffmpeg.exe and ratcapture.exe must not outlive it.
        private void OnApplicationQuit()
        {
            // The helper gets time to restore the program's volume.
            if (_capture != null) _capture.WaitOnStop = true;
            LocalStop();
            StopNetwork();
        }

        private void OnDestroy()
        {
            LocalStop();
            StopNetwork();
            var app = AppUtil.Instance;
            if (app == null) return;
            app.GamePaused -= OnGamePaused;
            app.GameUnpaused -= OnGameUnpaused;
        }
    }
}
