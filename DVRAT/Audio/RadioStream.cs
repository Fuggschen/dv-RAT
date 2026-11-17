using System;
using UnityEngine;
using UnityEngine.Networking;

namespace DVRAT
{
    /// <summary>Streams one station, following a playlist link first. Call <see cref="Update"/> every frame. Data arrives on the main thread.</summary>
    public class RadioStream
    {
        /// <summary>Silence for this long counts as unreachable.</summary>
        private const float ConnectTimeoutSeconds = 15f;
        private const int PlaylistTimeoutSeconds = 15;
        private const string UserAgent = "DV-RAT/0.1";

        private readonly Action<string> _onStart;
        private readonly Action<byte[], int, int> _onAudio;
        private UnityWebRequest _request;
        private bool _resolving;
        private IcyStream _icy;
        private float _startedAt;

        /// <param name="onStart">Called with the Content-Type before the first audio.</param>
        public RadioStream(Action<string> onStart, Action<byte[], int, int> onAudio)
        {
            _onStart = onStart;
            _onAudio = onAudio;
        }

        /// <summary>Null until the station sends one.</summary>
        public string Title { get; private set; }

        /// <summary>Null while all is well.</summary>
        public string Error { get; private set; }

        public bool Receiving => _icy != null;

        /// <summary>The station closed the stream.</summary>
        public bool Ended { get; private set; }

        public void Start(string url)
        {
            _startedAt = Time.unscaledTime;
            if (RadioPlaylist.IsPlaylistUrl(url))
            {
                _resolving = true;
                _request = UnityWebRequest.Get(url);
                _request.timeout = PlaylistTimeoutSeconds;
                _request.SetRequestHeader("User-Agent", UserAgent);
                _request.SendWebRequest();
            }
            else
            {
                Open(url);
            }
        }

        public void Update()
        {
            if (_request == null || Error != null || Ended) return;

            if (_resolving)
            {
                if (!_request.isDone) return;
                string text = Failed(_request) ? null : _request.downloadHandler.text;
                string stream = RadioPlaylist.FirstStream(text);
                if (Failed(_request))
                    Error = "Station list unreachable: " + _request.error;
                else if (stream == null)
                    Error = "The station's playlist has no stream this tablet can play";
                _request.Dispose();
                _request = null;
                _resolving = false;
                if (Error == null)
                    Open(stream);
                return;
            }

            if (_request.isDone)
            {
                if (Failed(_request))
                    Error = "Station unreachable: " + _request.error;
                else
                    Ended = true;
            }
            else if (_icy == null && Time.unscaledTime - _startedAt > ConnectTimeoutSeconds)
            {
                Error = "No answer from the station";
            }
        }

        public void Stop()
        {
            if (_request == null) return;
            _request.Abort();
            _request.Dispose();
            _request = null;
        }

        private void Open(string url)
        {
            _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new Receiver(this),
                // A live stream never finishes.
                timeout = 0,
            };
            _request.SetRequestHeader("Icy-MetaData", "1");
            _request.SetRequestHeader("User-Agent", UserAgent);
            _request.SendWebRequest();
        }

        private void Receive(byte[] data, int count)
        {
            if (_icy == null)
            {
                int.TryParse(_request.GetResponseHeader("icy-metaint"), out int metaInt);
                _onStart(_request.GetResponseHeader("Content-Type"));
                _icy = new IcyStream(metaInt, _onAudio, title => Title = title);
            }
            _icy.Push(data, count);
        }

        private static bool Failed(UnityWebRequest request) => request.isNetworkError || request.isHttpError;

        private class Receiver : DownloadHandlerScript
        {
            private readonly RadioStream _owner;

            public Receiver(RadioStream owner) : base(new byte[16384]) => _owner = owner;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0) return true;
                _owner.Receive(data, dataLength);
                return true;
            }
        }
    }
}
