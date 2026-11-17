using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    /// <summary>Reads the game's Radio.pls and reads and writes stations.json, <c>{"stations": [{"name": "...", "url": "..."}]}</c>.</summary>
    public static class StationList
    {
        private static readonly Regex PlsEntry = new Regex(@"^\s*(File|Title)(\d+)\s*=\s*(.*?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Entries without a TitleN are named by their URL.</summary>
        public static List<RadioStation> FromPls(string text)
        {
            var urls = new SortedDictionary<int, string>();
            var titles = new Dictionary<int, string>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                var match = PlsEntry.Match(line);
                if (!match.Success) continue;
                int number = int.Parse(match.Groups[2].Value);
                if (match.Groups[1].Value.Equals("File", StringComparison.OrdinalIgnoreCase))
                    urls[number] = match.Groups[3].Value;
                else
                    titles[number] = match.Groups[3].Value;
            }
            return urls
                .Where(e => IsStreamLink(e.Value))
                .Select(e => new RadioStation(titles.TryGetValue(e.Key, out string title) && title.Length > 0 ? title : e.Value, e.Value))
                .ToList();
        }

        /// <summary>Entries without a usable URL are skipped and added to <paramref name="problems"/>.</summary>
        public static List<RadioStation> FromJson(string json, ICollection<string> problems)
        {
            var stations = new List<RadioStation>();
            JToken root;
            try
            {
                root = JToken.Parse(json ?? "");
            }
            catch (JsonException ex)
            {
                problems.Add("stations.json is not valid JSON: " + ex.Message);
                return stations;
            }

            if (!(root is JObject obj) || !(obj["stations"] is JArray list))
            {
                problems.Add("stations.json needs a \"stations\" list");
                return stations;
            }
            int index = 0;
            foreach (var entry in list)
            {
                index++;
                string url = (entry as JObject)?.Value<string>("url")?.Trim();
                string name = (entry as JObject)?.Value<string>("name")?.Trim();
                if (!IsStreamLink(url))
                {
                    problems.Add($"stations.json entry {index} has no http or https \"url\"");
                    continue;
                }
                stations.Add(new RadioStation(string.IsNullOrEmpty(name) ? url : name, url));
            }
            return stations;
        }

        public static string ToJson(IEnumerable<RadioStation> stations)
        {
            var list = new JArray(stations.Select(s => new JObject { ["name"] = s.Name, ["url"] = s.Url }));
            return new JObject { ["stations"] = list }.ToString(Formatting.Indented);
        }

        private static bool IsStreamLink(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
