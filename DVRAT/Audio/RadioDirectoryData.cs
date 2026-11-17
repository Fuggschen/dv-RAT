using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    /// <summary>A station from the radio-browser.info directory.</summary>
    public class DirectoryStation : RadioStation
    {
        public DirectoryStation(string name, string url, string uuid, string codec, int bitrate, string countryCode)
            : base(name, url)
        {
            Uuid = uuid;
            Codec = codec;
            Bitrate = bitrate;
            CountryCode = countryCode;
        }

        public string Uuid { get; }
        public string Codec { get; }
        /// <summary>kbit/s, 0 when unknown.</summary>
        public int Bitrate { get; }
        public string CountryCode { get; }
    }

    /// <summary>A genre or a country.</summary>
    public class DirectoryCategory
    {
        public DirectoryCategory(string name, string id, int stations)
        {
            Name = name;
            Id = id;
            Stations = stations;
        }

        public string Name { get; }
        /// <summary>The tag for a genre, the ISO code for a country.</summary>
        public string Id { get; }
        public int Stations { get; }
    }

    /// <summary>Request URLs and answer parsing for https://api.radio-browser.info. Only codecs the bundled ffmpeg decodes are kept.</summary>
    public static class RadioDirectoryData
    {
        public const string ServerListUrl = "https://all.api.radio-browser.info/json/servers";
        public const string FallbackServer = "de1.api.radio-browser.info";

        private static readonly HashSet<string> PlayableCodecs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MP3", "AAC", "AAC+", "OGG", "OPUS" };

        public static string GenresUrl(string server, int limit) =>
            $"https://{server}/json/tags?order=stationcount&reverse=true&hidebroken=true&limit={limit}";

        public static string CountriesUrl(string server, int limit) =>
            $"https://{server}/json/countries?order=stationcount&reverse=true&hidebroken=true&limit={limit}";

        public static string StationsByGenreUrl(string server, string tag, int limit) =>
            $"https://{server}/json/stations/search?tag={Uri.EscapeDataString(tag)}&tagExact=true{StationQuery(limit)}";

        public static string StationsByCountryUrl(string server, string countryCode, int limit) =>
            $"https://{server}/json/stations/search?countrycode={Uri.EscapeDataString(countryCode)}{StationQuery(limit)}";

        /// <summary>Reporting plays makes popular stations rise in the lists.</summary>
        public static string ClickUrl(string server, string uuid) => $"https://{server}/json/url/{Uri.EscapeDataString(uuid)}";

        // Over-fetch, since the codec filter drops some.
        private static string StationQuery(int limit) => $"&order=clickcount&reverse=true&hidebroken=true&limit={limit * 2}";

        public static List<string> Servers(string json)
        {
            var servers = new List<string>();
            foreach (var entry in Array(json))
            {
                string name = (entry as JObject)?.Value<string>("name");
                if (!string.IsNullOrEmpty(name) && !servers.Contains(name))
                    servers.Add(name);
            }
            return servers;
        }

        public static List<DirectoryCategory> Genres(string json) => Categories(json, "name");

        public static List<DirectoryCategory> Countries(string json) => Categories(json, "iso_3166_1");

        public static List<DirectoryStation> Stations(string json, int limit)
        {
            var stations = new List<DirectoryStation>();
            foreach (var entry in Array(json))
            {
                if (stations.Count >= limit) break;
                if (!(entry is JObject s)) continue;
                string url = s.Value<string>("url_resolved");
                if (string.IsNullOrEmpty(url)) url = s.Value<string>("url");
                string codec = s.Value<string>("codec") ?? "";
                string name = (s.Value<string>("name") ?? "").Trim();
                if (s.Value<int?>("hls") == 1 || s.Value<int?>("lastcheckok") == 0) continue;
                if (!PlayableCodecs.Contains(codec) || name.Length == 0 || !IsStreamLink(url)) continue;
                stations.Add(new DirectoryStation(name, url, s.Value<string>("stationuuid"), codec,
                    s.Value<int?>("bitrate") ?? 0, s.Value<string>("countrycode") ?? ""));
            }
            return stations;
        }

        private static List<DirectoryCategory> Categories(string json, string idKey)
        {
            var list = new List<DirectoryCategory>();
            foreach (var entry in Array(json))
            {
                if (!(entry is JObject c)) continue;
                string name = (c.Value<string>("name") ?? "").Trim();
                string id = (c.Value<string>(idKey) ?? "").Trim();
                if (name.Length == 0 || id.Length == 0) continue;
                list.Add(new DirectoryCategory(Capitalise(name), id, c.Value<int?>("stationcount") ?? 0));
            }
            return list;
        }

        private static string Capitalise(string name) =>
            char.ToUpper(name[0], CultureInfo.InvariantCulture) + name.Substring(1);

        private static JArray Array(string json)
        {
            try
            {
                return JToken.Parse(json ?? "") as JArray ?? new JArray();
            }
            catch (JsonException)
            {
                return new JArray();
            }
        }

        private static bool IsStreamLink(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
