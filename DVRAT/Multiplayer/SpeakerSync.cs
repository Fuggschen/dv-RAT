using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Syncs speaker volume and EQ between players, like <see cref="TabletSync"/>. No Multiplayer types.</summary>
    public class SpeakerSync : MonoBehaviour
    {
        private const float PollSeconds = 0.2f;

        private static readonly List<SpeakerSync> Active = new List<SpeakerSync>();
        // A mount and its settings can arrive in either order.
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();

        private RATSpeaker _speaker;
        private string _known;
        private float _nextPoll;

        private void Awake() => _speaker = GetComponent<RATSpeaker>();

        private void OnEnable() => Active.Add(this);

        private void OnDisable()
        {
            Active.Remove(this);
            _known = null;
        }

        private void Update()
        {
            if (_speaker == null || !MultiplayerSync.InGame)
            {
                _known = null;
                return;
            }
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollSeconds;

            string id = _speaker.Settings.Id;
            if (Pending.TryGetValue(id, out string waiting))
            {
                Pending.Remove(id);
                Apply(waiting);
                return;
            }
            string state = Describe();
            if (state == _known) return;
            // The first look only records: a copy starts from its mount's settings.
            bool first = _known == null;
            _known = state;
            if (!first)
                MultiplayerSync.SendSpeaker(id, state);
        }

        private string Describe() => _speaker.SaveState().ToString(Formatting.None);

        private void Apply(string state)
        {
            try
            {
                _speaker.Settings.CopyFrom(SpeakerSettings.FromJson(JObject.Parse(state)));
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Multiplayer: speaker settings could not be read: {ex.Message}");
                return;
            }
            // So the takeover is not sent back.
            _known = Describe();
        }

        /// <summary>Taken over at the speaker's next look.</summary>
        public static void Receive(string id, string state)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(state)) return;
            Pending[id] = state;
        }

        /// <summary>For a player who joins.</summary>
        public static List<KeyValuePair<string, string>> AllStates()
        {
            var states = new List<KeyValuePair<string, string>>();
            foreach (var sync in Active)
                if (sync._speaker != null)
                    states.Add(new KeyValuePair<string, string>(sync._speaker.Settings.Id, sync.Describe()));
            return states;
        }

        public static void ForgetPending() => Pending.Clear();
    }
}
