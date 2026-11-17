using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Syncs session, view state and power between players' copies. Changes are found by comparing, not by hooking buttons. No Multiplayer types.</summary>
    public class TabletSync : MonoBehaviour
    {
        // A tap shows elsewhere within a ping or two. Comparing costs under 1 ms.
        private const float PollSeconds = 0.2f;

        private static readonly List<TabletSync> Active = new List<TabletSync>();
        // A mount and a state can arrive in either order.
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();

        private RATTabletController _tablet;
        // Last sent or taken over. Null until the first look in a game.
        private string _known;
        private float _nextPoll;

        private void Awake() => _tablet = GetComponent<RATTabletController>();

        private void OnEnable() => Active.Add(this);

        private void OnDisable()
        {
            Active.Remove(this);
            _known = null;
        }

        private void Update()
        {
            if (_tablet == null || !MultiplayerSync.InGame)
            {
                _known = null;
                return;
            }
            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollSeconds;

            if (Pending.TryGetValue(_tablet.Session.Id, out string waiting))
            {
                Pending.Remove(_tablet.Session.Id);
                Apply(waiting);
                return;
            }
            string state = Describe();
            if (state == _known) return;
            // The first look only records: the mount's state may be older than the host's next one.
            bool first = _known == null;
            string before = _known;
            _known = state;
            if (first) return;
            // Logged so a page that keeps writing while it draws shows up.
            RATMain.Log($"Multiplayer: tablet {_tablet.Session.Id} sends {string.Join(", ", ChangedKeys(before, state))}");
            MultiplayerSync.SendSession(_tablet.Session.Id, state);
        }

        private static IEnumerable<string> ChangedKeys(string before, string after)
        {
            var a = JObject.Parse(before);
            var b = JObject.Parse(after);
            return b.Properties().Select(p => p.Name).Union(a.Properties().Select(p => p.Name))
                .Where(key => !JToken.DeepEquals(a[key], b[key]));
        }

        private string Describe()
        {
            var json = _tablet.Session.ToSyncJson();
            json["power"] = _tablet.PowerSummary;
            json["media"] = _tablet.Media?.SyncState();
            return json.ToString(Formatting.None);
        }

        private void Apply(string state)
        {
            JObject json;
            try
            {
                json = JObject.Parse(state);
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Multiplayer: a tablet state could not be read: {ex.Message}");
                return;
            }
            var history = new List<string>(_tablet.Session.History);
            string planner = Planner(_tablet.Session);
            bool rebuild = NeedsRebuild(JObject.Parse(Describe()), json);
            try
            {
                _tablet.Session.ApplySync(json, Time.time);
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Multiplayer: a tablet state could not be taken over: {ex.Message}");
                return;
            }
            _tablet.ApplyRemotePower(json.Value<string>("power"));
            _tablet.Media?.ApplySync(json["media"] as JObject);
            if (Planner(_tablet.Session) != planner)
                _tablet.FollowSyncedRoute();
            _tablet.ShowSyncedSession(historyChanged: !history.SequenceEqual(_tablet.Session.History), rebuild);
            // So the takeover is not sent back.
            _known = Describe();
        }

        // These keys update on refresh, so a key pressed elsewhere does not rebuild the page.
        private static readonly HashSet<string> RefreshOnly = new HashSet<string> { "media", "power", "stopwatch", "settings" };
        private static readonly HashSet<string> LiveSettings = new HashSet<string> { MediaPlayer.VolumeSetting };

        private static bool NeedsRebuild(JObject before, JObject after)
        {
            foreach (var key in before.Properties().Select(p => p.Name).Union(after.Properties().Select(p => p.Name)))
            {
                if (JToken.DeepEquals(before[key], after[key])) continue;
                if (!RefreshOnly.Contains(key)) return true;
                if (key == "settings" && SettingsNeedRebuild(before[key] as JObject, after[key] as JObject)) return true;
            }
            return false;
        }

        // E.g. the spectrum changes the page layout. The volume only moves a bar.
        private static bool SettingsNeedRebuild(JObject before, JObject after)
        {
            before = before ?? new JObject();
            after = after ?? new JObject();
            foreach (var key in before.Properties().Select(p => p.Name).Union(after.Properties().Select(p => p.Name)))
                if (!LiveSettings.Contains(key) && !JToken.DeepEquals(before[key], after[key]))
                    return true;
            return false;
        }

        private static string Planner(TabletSession session) =>
            $"{session.PlannerFrom}|{string.Join(",", session.PlannerStops)}|{session.PlannerTo}|{session.PlannerHasRoute}|{session.PlannerUnderWire}";

        /// <summary>Taken over at the next look, never at once: a freshly mounted copy has not started, and starting would switch it off again.</summary>
        public static void Receive(string id, string state)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(state)) return;
            Pending[id] = state;
        }

        /// <summary>Goes with the mount. Null for a speaker.</summary>
        public static string StateOf(DV.Customization.Gadgets.GadgetBase gadget)
        {
            var sync = gadget != null ? gadget.GetComponentInChildren<TabletSync>(true) : null;
            return sync != null && sync._tablet != null ? sync.Describe() : null;
        }

        /// <summary>For a player who joins.</summary>
        public static List<KeyValuePair<string, string>> AllStates()
        {
            var states = new List<KeyValuePair<string, string>>();
            foreach (var sync in Active)
                if (sync._tablet != null)
                    states.Add(new KeyValuePair<string, string>(sync._tablet.Session.Id, sync.Describe()));
            return states;
        }

        /// <summary>Null when it is not mounted here.</summary>
        public static RATTabletController Find(string id)
        {
            foreach (var sync in Active)
                if (sync._tablet != null && sync._tablet.Session.Id == id)
                    return sync._tablet;
            return null;
        }

        public static void ForEachTablet(System.Action<RATTabletController> action)
        {
            foreach (var sync in Active.ToList())
                if (sync._tablet != null)
                    action(sync._tablet);
        }

        public static void ForgetPending() => Pending.Clear();
    }
}
