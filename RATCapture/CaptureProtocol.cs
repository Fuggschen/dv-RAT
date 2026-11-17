using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DVRAT
{
    public sealed class CaptureApp
    {
        public CaptureApp(int processId, string processName, string displayName, bool hasMedia)
        {
            ProcessId = processId;
            ProcessName = processName;
            DisplayName = displayName;
            HasMedia = hasMedia;
        }

        /// <summary>The first process of the program. Its helper processes play through it.</summary>
        public int ProcessId { get; }
        /// <summary>The exe's name without ".exe", e.g. "Spotify".</summary>
        public string ProcessName { get; }
        public string DisplayName { get; }
        /// <summary>True when Windows' media controls know the program.</summary>
        public bool HasMedia { get; }
    }

    public enum CaptureEventKind { Waiting, Capturing, Media, NoMedia, Error, Log }

    public sealed class CaptureEvent
    {
        public CaptureEventKind Kind { get; set; }
        /// <summary>Media only, e.g. "Playing" or "Paused".</summary>
        public string Status { get; set; }
        public string Artist { get; set; }
        public string Title { get; set; }
        /// <summary>Error and log only.</summary>
        public string Message { get; set; }
    }

    /// <summary>A session's channel levels before quieting, keyed by session identifier.</summary>
    public sealed class SavedLevels
    {
        public SavedLevels(string sessionIdentifier, float[] levels)
        {
            SessionIdentifier = sessionIdentifier;
            Levels = levels;
        }

        public string SessionIdentifier { get; }
        public float[] Levels { get; }
    }

    /// <summary>Lines between the mod and ratcapture.exe, one tab-separated line per item. Tabs and line breaks in names become spaces.</summary>
    public static class CaptureProtocol
    {
        public const string Play = "play";
        public const string Pause = "pause";
        public const string Next = "next";
        public const string Previous = "previous";
        public const string QuietOn = "quiet on";
        public const string QuietOff = "quiet off";

        private const char Tab = '\t';

        public static string AppLine(CaptureApp app) =>
            Join("app", app.ProcessId.ToString(CultureInfo.InvariantCulture), app.ProcessName, app.DisplayName, app.HasMedia ? "1" : "0");

        /// <summary>Null when not an app line.</summary>
        public static CaptureApp ParseApp(string line)
        {
            var parts = Split(line);
            if (parts.Length != 5 || parts[0] != "app" ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int processId))
                return null;
            return new CaptureApp(processId, parts[2], parts[3], parts[4] == "1");
        }

        public static string WaitingLine() => "waiting";
        public static string CapturingLine() => "capturing";
        public static string MediaLine(string status, string artist, string title) => Join("media", status, artist, title);
        public static string NoMediaLine() => "nomedia";
        public static string ErrorLine(string message) => Join("error", message);
        public static string LogLine(string message) => Join("log", message);

        /// <summary>Null for an unknown line, e.g. from a newer helper.</summary>
        public static CaptureEvent ParseEvent(string line)
        {
            var parts = Split(line);
            switch (parts[0])
            {
                case "waiting": return new CaptureEvent { Kind = CaptureEventKind.Waiting };
                case "capturing": return new CaptureEvent { Kind = CaptureEventKind.Capturing };
                case "nomedia": return new CaptureEvent { Kind = CaptureEventKind.NoMedia };
                case "media" when parts.Length == 4:
                    return new CaptureEvent { Kind = CaptureEventKind.Media, Status = parts[1], Artist = parts[2], Title = parts[3] };
                case "error" when parts.Length == 2: return new CaptureEvent { Kind = CaptureEventKind.Error, Message = parts[1] };
                case "log" when parts.Length == 2: return new CaptureEvent { Kind = CaptureEventKind.Log, Message = parts[1] };
                default: return null;
            }
        }

        public static string LevelsLine(SavedLevels saved) =>
            Join(saved.SessionIdentifier, string.Join(";", saved.Levels.Select(l => l.ToString("R", CultureInfo.InvariantCulture))));

        public static SavedLevels ParseLevels(string line)
        {
            var parts = Split(line);
            if (parts.Length != 2 || parts[0].Length == 0) return null;
            var levels = new List<float>();
            foreach (string level in parts[1].Split(';'))
            {
                if (!float.TryParse(level, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0f || value > 1f)
                    return null;
                levels.Add(value);
            }
            return levels.Count > 0 ? new SavedLevels(parts[0], levels.ToArray()) : null;
        }

        private static string Join(params string[] parts) => string.Join(Tab.ToString(), parts.Select(Clean));

        private static string Clean(string part) =>
            part == null ? "" : part.Replace(Tab, ' ').Replace('\r', ' ').Replace('\n', ' ');

        private static string[] Split(string line) => (line ?? "").Split(Tab);
    }
}
