using System;
using System.Linq;
using Windows.Foundation;
using Windows.Media.Control;

namespace RatCapture
{
    /// <summary>
    /// A program is matched when its app id contains the exe name. Programs with an unrelated id, like Firefox, get no session.
    /// Waiting calls throw <see cref="TimeoutException"/> after <see cref="TimeoutMs"/>.
    /// </summary>
    public sealed class MediaControls
    {
        public const int TimeoutMs = 2000;

        private readonly GlobalSystemMediaTransportControlsSessionManager _manager;

        public MediaControls()
        {
            _manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
        }

        public GlobalSystemMediaTransportControlsSession Session(string processName) =>
            _manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId.IndexOf(processName, StringComparison.OrdinalIgnoreCase) >= 0);

        public bool HasSession(string processName) => Session(processName) != null;

        /// <summary>Null when the program has no media session.</summary>
        public (string Status, string Artist, string Title)? Now(string processName)
        {
            var session = Session(processName);
            if (session == null) return null;
            var info = Wait(session.TryGetMediaPropertiesAsync());
            return (session.GetPlaybackInfo().PlaybackStatus.ToString(), info.Artist, info.Title);
        }

        /// <summary>False when the program refused or has no session.</summary>
        public bool Send(string processName, string key)
        {
            var session = Session(processName);
            if (session == null) return false;
            var call = key == DVRAT.CaptureProtocol.Play ? session.TryPlayAsync()
                : key == DVRAT.CaptureProtocol.Pause ? session.TryPauseAsync()
                : key == DVRAT.CaptureProtocol.Next ? session.TrySkipNextAsync()
                : key == DVRAT.CaptureProtocol.Previous ? session.TrySkipPreviousAsync()
                : throw new ArgumentException("Unknown key " + key);
            return Wait(call);
        }

        private static T Wait<T>(IAsyncOperation<T> operation)
        {
            var task = operation.AsTask();
            try
            {
                if (!task.Wait(TimeoutMs)) throw new TimeoutException();
            }
            catch (AggregateException)
            {
                // GetResult below rethrows the failure unwrapped.
            }
            return task.GetAwaiter().GetResult();
        }
    }
}
