using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    /// <summary>State one tablet keeps across power cycles and savegames. No Unity types, so it is unit testable.</summary>
    public class TabletSession
    {
        /// <summary>Names this tablet in multiplayer, where each player has a copy. Stays the same through saves and the inventory.</summary>
        [JsonProperty("id")] public string Id { get; set; } = NewId();

        public static string NewId() => System.Guid.NewGuid().ToString("N");

        [JsonProperty("on")] public bool PoweredOn { get; set; }

        /// <summary>Page IDs, oldest first. The last is the open page.</summary>
        [JsonProperty("history")] public List<string> History { get; set; } = new List<string>();

        [JsonProperty("vars")] public Dictionary<string, Dictionary<string, string>> PageVars { get; set; } =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Yard ID of the planner's start. Null means the tablet's own train.</summary>
        [JsonProperty("plannerFrom")] public string PlannerFrom { get; set; }
        [JsonProperty("plannerTo")] public string PlannerTo { get; set; }

        [JsonProperty("plannerStops")] public List<string> PlannerStops { get; set; } = new List<string>();

        [JsonProperty("plannerRoute")] public bool PlannerHasRoute { get; set; }

        /// <summary>Keeps the planner to tracks under the 1500 V DC Catenary mod's wire.</summary>
        [JsonProperty("plannerUnderWire")] public bool PlannerUnderWire { get; set; }

        [JsonProperty("stopwatch")] public Stopwatch Stopwatch { get; set; } = new Stopwatch();

        /// <summary>Values the player changed. The rest come from <see cref="DefaultSettings"/>.</summary>
        [JsonProperty("settings")] public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();

        /// <summary>Value of every setting the player never changed. Pages read them as <c>settings.name</c>.</summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultSettings = new Dictionary<string, string>
        {
            // Route timetable: show what each signal shows, or only that one is there.
            ["showSignalAspects"] = "true",
            // Route timetable: a ¥ row where the train end passes a raised speed limit.
            ["showTrainClear"] = "true",
            // Route timetable: switches set against the planned route show red.
            ["markMisalignedSwitches"] = "true",
            // Needs Junction Maintenance: lists damaged and locked switches ahead.
            ["showSwitchDamage"] = "true",
            // Needs Track Maintenance: damaged track ahead counts as a speed restriction.
            ["showTrackDamage"] = "true",
            // "inverted" turns the screen upside down, for a roof-mounted tablet.
            ["rotation"] = "standard",
            // light, dark, hotdog, colorful or pastel.
            ["theme"] = "light",
            // simple, win2000, round, outline or pill.
            ["buttons"] = "simple",
            // Percent, 10 to 100.
            ["brightness"] = "100",
            // Yard ID for the Station and Yard pages. Empty means the nearest.
            ["station"] = "",
            // Percent, 0 to 100.
            ["mediaVolume"] = "80",
            // Last program PC Audio played, as "process|display name".
            ["pcAudioApp"] = "",
        };

        public string Setting(string name) =>
            Settings.TryGetValue(name, out string value) ? value : DefaultSettings.TryGetValue(name, out value) ? value : "";

        public Dictionary<string, string> AllSettings()
        {
            var all = DefaultSettings.ToDictionary(e => e.Key, e => e.Value);
            foreach (var entry in Settings)
                all[entry.Key] = entry.Value;
            return all;
        }

        /// <summary>Page variables, created from <paramref name="defaults"/> on first use. Defaults added since the save are filled in.</summary>
        public Dictionary<string, string> Vars(string pageId, IDictionary<string, string> defaults)
        {
            if (!PageVars.TryGetValue(pageId, out var vars))
                PageVars[pageId] = vars = new Dictionary<string, string>();
            foreach (var entry in defaults ?? new Dictionary<string, string>())
                if (!vars.ContainsKey(entry.Key))
                    vars[entry.Key] = entry.Value;
            return vars;
        }

        /// <summary>List screens, open sub-views and scroll positions. Not saved. Write it only on player actions, never while drawing, so multiplayer copies stay equal.</summary>
        [JsonIgnore] public Dictionary<string, Dictionary<string, string>> ViewVars { get; set; } =
            new Dictionary<string, Dictionary<string, string>>();

        public Dictionary<string, string> View(string pageId)
        {
            if (!ViewVars.TryGetValue(pageId, out var view))
                ViewVars[pageId] = view = new Dictionary<string, string>();
            return view;
        }

        public const string ListKeyPrefix = "list:";

        /// <summary>Puts a page's lists back on their first screen. Sub-views stay.</summary>
        public void ResetLists(string pageId)
        {
            if (!ViewVars.TryGetValue(pageId, out var view)) return;
            foreach (string key in view.Keys.Where(k => k.StartsWith(ListKeyPrefix, System.StringComparison.Ordinal)).ToList())
                view.Remove(key);
        }

        public JObject ToJson() => JObject.FromObject(this);

        /// <summary>Page variables private to each player, e.g. the open music folder. Never synced.</summary>
        public static readonly HashSet<string> LocalVarKeys = new HashSet<string> { "mediaFolder" };

        /// <summary>Settings tied to the player's own PC. Never synced.</summary>
        public static readonly HashSet<string> LocalSettingKeys = new HashSet<string> { "pcAudioApp" };

        // Saved properties that identify the tablet. A sync leaves them alone.
        private static readonly HashSet<string> IdentityKeys = new HashSet<string> { "id" };

        /// <summary>State shared between multiplayer copies: the saved state minus identity and local keys, plus the view state.</summary>
        public JObject ToSyncJson()
        {
            var json = ToJson();
            foreach (string key in IdentityKeys)
                json.Remove(key);
            if (json["vars"] is JObject vars)
                foreach (var page in vars.Properties())
                    if (page.Value is JObject pageVars)
                        foreach (string key in LocalVarKeys)
                            pageVars.Remove(key);
            if (json["settings"] is JObject settings)
                foreach (string key in LocalSettingKeys)
                    settings.Remove(key);
            json["view"] = JObject.FromObject(ViewVars);
            return json;
        }

        /// <summary>Takes over another player's <see cref="ToSyncJson"/>, keeping this player's local keys. <paramref name="now"/> resumes a running stopwatch.</summary>
        public void ApplySync(JObject json, float now)
        {
            var local = PageVars.SelectMany(page => page.Value.Where(entry => LocalVarKeys.Contains(entry.Key))
                .Select(entry => (page: page.Key, entry.Key, entry.Value))).ToList();
            var localSettings = Settings.Where(entry => LocalSettingKeys.Contains(entry.Key)).ToList();
            var other = FromJson(json);
            foreach (var property in typeof(TabletSession).GetProperties())
            {
                var attribute = (JsonPropertyAttribute)System.Attribute.GetCustomAttribute(property, typeof(JsonPropertyAttribute));
                if (attribute == null || IdentityKeys.Contains(attribute.PropertyName) || !property.CanWrite) continue;
                property.SetValue(this, property.GetValue(other));
            }
            ViewVars = json["view"]?.ToObject<Dictionary<string, Dictionary<string, string>>>()
                       ?? new Dictionary<string, Dictionary<string, string>>();
            foreach (var (page, key, value) in local)
                Vars(page, null)[key] = value;
            foreach (string key in LocalSettingKeys)
                Settings.Remove(key);
            foreach (var entry in localSettings)
                Settings[entry.Key] = entry.Value;
            Stopwatch.Resume(now);
        }

        /// <summary>The session in <paramref name="json"/>, or a new one when it is missing.</summary>
        public static TabletSession FromJson(JToken json)
        {
            var session = json?.ToObject<TabletSession>();
            if (session == null) return new TabletSession();
            if (string.IsNullOrEmpty(session.Id)) session.Id = NewId();
            session.History = (session.History ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).ToList();
            session.PageVars = session.PageVars ?? new Dictionary<string, Dictionary<string, string>>();
            session.Stopwatch = session.Stopwatch ?? new Stopwatch();
            session.Settings = session.Settings ?? new Dictionary<string, string>();
            return session;
        }
    }

    /// <summary>Stopwatch. <c>now</c> is any clock in seconds, e.g. Time.time.</summary>
    public class Stopwatch
    {
        [JsonProperty("elapsed")] private float _stored;
        [JsonProperty("running")] public bool Running { get; private set; }

        // Not saved. The clock restarts every game session.
        private float _startedAt;
        private bool _started;

        public float Elapsed(float now) => _stored + (Running && _started ? now - _startedAt : 0f);

        public void Start(float now)
        {
            if (Running && _started) return;
            Running = true;
            _started = true;
            _startedAt = now;
        }

        public void Stop(float now)
        {
            _stored = Elapsed(now);
            Running = false;
        }

        public void Reset(float now)
        {
            _stored = 0f;
            _startedAt = now;
        }

        /// <summary>Folds the running time into the stored time so it can be saved.</summary>
        public void Checkpoint(float now)
        {
            _stored = Elapsed(now);
            _startedAt = now;
        }

        /// <summary>Continues a watch loaded as running.</summary>
        public void Resume(float now)
        {
            if (!Running || _started) return;
            _started = true;
            _startedAt = now;
        }
    }
}
