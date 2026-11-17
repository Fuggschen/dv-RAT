using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// The game's stations (Radio.pls) and the player's (radio/stations.json in the mod folder). The mod never ships
    /// that file, so updates keep the player's stations.
    /// </summary>
    public static class RadioStations
    {
        public const string FolderName = "radio";
        public const string FileName = "stations.json";

        private static List<RadioStation> _game;
        private static List<RadioStation> _mine;

        public static IReadOnlyList<RadioStation> Game
        {
            get
            {
                if (_game == null) Reload();
                return _game;
            }
        }

        public static IReadOnlyList<RadioStation> Mine
        {
            get
            {
                if (_mine == null) Reload();
                return _mine;
            }
        }

        public static string MyStationsPath =>
            RATMain.ModEntry != null ? Path.Combine(RATMain.ModEntry.Path, FolderName, FileName) : null;

        private static string GamePlaylistPath => Path.Combine(Application.streamingAssetsPath, "music", "Radio.pls");

        /// <summary>Rereads both lists, e.g. after stations.json was edited.</summary>
        public static void Reload()
        {
            _game = ReadGame();
            _mine = ReadMine();
        }

        public static bool IsMine(RadioStation station)
        {
            foreach (var mine in Mine)
                if (mine.Url == station.Url) return true;
            return false;
        }

        /// <summary>Saves immediately. A station with the same URL is not added twice.</summary>
        public static void AddToMine(RadioStation station)
        {
            if (station == null || IsMine(station)) return;
            _mine.Add(station);
            SaveMine();
        }

        public static void RemoveFromMine(RadioStation station)
        {
            if (station == null || _mine.RemoveAll(s => s.Url == station.Url) == 0) return;
            SaveMine();
        }

        private static List<RadioStation> ReadGame()
        {
            try
            {
                string path = GamePlaylistPath;
                return File.Exists(path) ? StationList.FromPls(File.ReadAllText(path)) : new List<RadioStation>();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                RATMain.LogWarning($"Reading the game's radio stations failed: {ex.Message}");
                return new List<RadioStation>();
            }
        }

        private static List<RadioStation> ReadMine()
        {
            string path = MyStationsPath;
            if (path == null) return new List<RadioStation>();
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, StationList.ToJson(new RadioStation[0]));
                    return new List<RadioStation>();
                }
                var problems = new List<string>();
                var stations = StationList.FromJson(File.ReadAllText(path), problems);
                foreach (string problem in problems)
                    RATMain.LogWarning($"{path}: {problem}");
                return stations;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                RATMain.LogWarning($"Reading {path} failed: {ex.Message}");
                return new List<RadioStation>();
            }
        }

        private static void SaveMine()
        {
            string path = MyStationsPath;
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, StationList.ToJson(_mine));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                RATMain.LogWarning($"Saving {path} failed: {ex.Message}");
            }
        }
    }
}
