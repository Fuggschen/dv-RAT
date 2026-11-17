using System;
using System.Collections.Generic;
using UnityEngine.Networking;

namespace DVRAT
{
    /// <summary>An answer filled in when it arrives. The page polls it on refresh.</summary>
    public class DirectoryQuery<T> where T : class
    {
        public bool Done { get; internal set; }
        /// <summary>Null until <see cref="Done"/> and on failure.</summary>
        public T Result { get; internal set; }
        /// <summary>Null unless the request failed.</summary>
        public string Error { get; internal set; }
    }

    /// <summary>
    /// Queries radio-browser.info, see <see cref="RadioDirectoryData"/>. One server is picked per session and answers
    /// are cached. Requests send only a generic User-Agent.
    /// </summary>
    public static class RadioDirectory
    {
        /// <summary>Per list.</summary>
        public const int ListLimit = 40;
        private const int TimeoutSeconds = 15;
        private const string UserAgent = "DV-RAT/0.1";

        private static string _server;
        private static bool _findingServer;
        private static readonly List<Action> WaitingForServer = new List<Action>();
        private static readonly Dictionary<string, object> Answers = new Dictionary<string, object>();
        private static readonly Random Random = new Random();

        public static DirectoryQuery<List<DirectoryCategory>> Genres() =>
            Ask(server => RadioDirectoryData.GenresUrl(server, ListLimit), RadioDirectoryData.Genres, "genres");

        public static DirectoryQuery<List<DirectoryCategory>> Countries() =>
            Ask(server => RadioDirectoryData.CountriesUrl(server, ListLimit), RadioDirectoryData.Countries, "countries");

        public static DirectoryQuery<List<DirectoryStation>> StationsByGenre(string tag) =>
            Ask(server => RadioDirectoryData.StationsByGenreUrl(server, tag, ListLimit),
                json => RadioDirectoryData.Stations(json, ListLimit), "genre:" + tag);

        public static DirectoryQuery<List<DirectoryStation>> StationsByCountry(string countryCode) =>
            Ask(server => RadioDirectoryData.StationsByCountryUrl(server, countryCode, ListLimit),
                json => RadioDirectoryData.Stations(json, ListLimit), "country:" + countryCode);

        /// <summary>Drops a cached answer so the next ask hits the directory again (Retry).</summary>
        /// <param name="list">"genres" or "countries". With a category, that category's station list.</param>
        public static void Forget(string list, DirectoryCategory category)
        {
            string key = category == null ? list : (list == "genres" ? "genre:" : "country:") + category.Id;
            Answers.Remove(key);
        }

        /// <summary>The directory asks apps to report plays. The answer is ignored.</summary>
        public static void CountPlay(DirectoryStation station)
        {
            if (string.IsNullOrEmpty(station?.Uuid)) return;
            WithServer(server =>
            {
                var request = Request(RadioDirectoryData.ClickUrl(server, station.Uuid));
                request.SendWebRequest().completed += _ => request.Dispose();
            });
        }

        private static DirectoryQuery<T> Ask<T>(Func<string, string> url, Func<string, T> parse, string key) where T : class
        {
            // Failed and pending answers are kept too. See Forget.
            if (Answers.TryGetValue(key, out object known) && known is DirectoryQuery<T> kept)
                return kept;
            var query = new DirectoryQuery<T>();
            Answers[key] = query;
            WithServer(server =>
            {
                var request = Request(url(server));
                request.SendWebRequest().completed += _ =>
                {
                    if (request.isNetworkError || request.isHttpError)
                        query.Error = "Radio directory unreachable: " + request.error;
                    else
                        query.Result = parse(request.downloadHandler.text);
                    query.Done = true;
                    request.Dispose();
                };
            });
            return query;
        }

        private static void WithServer(Action<string> run)
        {
            if (_server != null)
            {
                run(_server);
                return;
            }
            WaitingForServer.Add(() => run(_server));
            if (_findingServer) return;
            _findingServer = true;

            var request = Request(RadioDirectoryData.ServerListUrl);
            request.SendWebRequest().completed += _ =>
            {
                var servers = request.isNetworkError || request.isHttpError
                    ? new List<string>()
                    : RadioDirectoryData.Servers(request.downloadHandler.text);
                request.Dispose();
                _server = servers.Count > 0 ? servers[Random.Next(servers.Count)] : RadioDirectoryData.FallbackServer;
                _findingServer = false;
                var waiting = new List<Action>(WaitingForServer);
                WaitingForServer.Clear();
                foreach (var action in waiting)
                    action();
            };
        }

        private static UnityWebRequest Request(string url)
        {
            var request = UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;
            request.SetRequestHeader("User-Agent", UserAgent);
            return request;
        }
    }
}
