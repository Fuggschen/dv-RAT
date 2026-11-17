using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>The source copy's media state, see <see cref="MediaPlayer.SyncState"/>. The JSON is kept as received, so sending it back creates no sync loop.</summary>
    public class RemoteMedia
    {
        // See MediaPlayer.SyncState.
        public const string SourceKey = "source", StateKey = "state", FileKey = "file", RadioTitleKey = "radioTitle",
            StationNameKey = "stationName", StationUrlKey = "stationUrl", CaptureProcessKey = "captureProcess",
            CaptureDisplayKey = "captureDisplay", CaptureMediaKey = "captureMedia", CaptureTitleKey = "captureTitle",
            CaptureStatusKey = "captureStatus", WaitingKey = "waiting", ConnectingKey = "connecting",
            BufferingKey = "buffering", DurationKey = "duration", PositionKey = "position", ShuffleKey = "shuffle",
            ErrorKey = "error", CanPlayKey = "canPlay", CanPauseKey = "canPause", CanNextKey = "canNext",
            CanPreviousKey = "canPrevious";

        private readonly float _receivedAt;

        public RemoteMedia(JObject json, float now)
        {
            Json = json;
            _receivedAt = now;
            Source = json.Value<string>(SourceKey);
            State = System.Enum.TryParse(json.Value<string>(StateKey), out PlayState state) ? state : PlayState.Stopped;
            CurrentFile = json.Value<string>(FileKey);
            RadioTitle = json.Value<string>(RadioTitleKey);
            string url = json.Value<string>(StationUrlKey);
            LastStation = url != null ? new RadioStation(json.Value<string>(StationNameKey) ?? url, url) : null;
            string process = json.Value<string>(CaptureProcessKey);
            LastCaptureApp = process != null
                ? new CaptureApp(0, process, json.Value<string>(CaptureDisplayKey) ?? process, json.Value<bool?>(CaptureMediaKey) ?? false)
                : null;
            CaptureTitle = json.Value<string>(CaptureTitleKey);
            CaptureStatus = json.Value<string>(CaptureStatusKey);
            CaptureWaiting = json.Value<bool?>(WaitingKey) ?? false;
            RadioConnecting = json.Value<bool?>(ConnectingKey) ?? false;
            RadioBuffering = json.Value<bool?>(BufferingKey) ?? false;
            Duration = json.Value<float?>(DurationKey) ?? 0f;
            _position = json.Value<float?>(PositionKey) ?? 0f;
            Shuffle = json.Value<bool?>(ShuffleKey) ?? false;
            Error = json.Value<string>(ErrorKey);
            CanPlay = json.Value<bool?>(CanPlayKey) ?? false;
            CanPause = json.Value<bool?>(CanPauseKey) ?? false;
            CanNext = json.Value<bool?>(CanNextKey) ?? false;
            CanPrevious = json.Value<bool?>(CanPreviousKey) ?? false;
        }

        public JObject Json { get; }

        /// <summary>Name of the player whose copy plays.</summary>
        public string Source { get; }

        public PlayState State { get; }
        public string CurrentFile { get; }
        public string RadioTitle { get; }
        public RadioStation LastStation { get; }
        public RadioStation Station => State != PlayState.Stopped ? LastStation : null;
        public CaptureApp LastCaptureApp { get; }
        public CaptureApp CaptureApp => State != PlayState.Stopped ? LastCaptureApp : null;
        public string CaptureTitle { get; }
        public string CaptureStatus { get; }
        public bool CaptureWaiting { get; }
        public bool RadioConnecting { get; }
        public bool RadioBuffering { get; }
        public float Duration { get; }
        public bool Shuffle { get; }
        public string Error { get; }
        public bool CanStop => State != PlayState.Stopped;
        public bool CanPlay { get; }
        public bool CanPause { get; }
        public bool CanNext { get; }
        public bool CanPrevious { get; }

        private readonly float _position;

        /// <summary>Extrapolated from the position the source sent.</summary>
        public float Position
        {
            get
            {
                if (State == PlayState.Stopped) return 0f;
                float position = _position + (State == PlayState.Playing ? Time.time - _receivedAt : 0f);
                return Duration > 0f ? Mathf.Min(position, Duration) : position;
            }
        }
    }
}
