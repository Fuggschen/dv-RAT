using System.Globalization;
using System.Text.RegularExpressions;

namespace DVRAT
{
    /// <summary>Parses ffmpeg stderr run with <c>-loglevel level+info</c>, so every line starts with its level.</summary>
    public static class FfmpegLog
    {
        private static readonly Regex DurationLine =
            new Regex(@"\[info\]\s+Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.CultureInvariant);

        private static readonly Regex ProblemLine = new Regex(@"\[(error|fatal)\]\s*(.+)$", RegexOptions.CultureInvariant);

        /// <summary>The input's length in seconds from its "Duration" line; null for other lines and for "Duration: N/A".</summary>
        public static double? Duration(string line)
        {
            var match = DurationLine.Match(line ?? "");
            if (!match.Success) return null;
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
                   + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
                   + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>The message of an error or fatal line without its prefix; null for other lines.</summary>
        public static string Problem(string line)
        {
            var match = ProblemLine.Match(line ?? "");
            return match.Success ? match.Groups[2].Value.Trim() : null;
        }
    }
}
