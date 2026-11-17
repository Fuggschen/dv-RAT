using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DVRAT
{
    /// <summary>Parses <c>[[page-id]]</c> and <c>[[page-id|caption]]</c>. Without a caption the target's title shows.</summary>
    public static class LinkParser
    {
        private static readonly Regex LinkPattern = new Regex(@"\[\[([^\]|]+)(?:\|([^\]]*))?\]\]", RegexOptions.Compiled);

        public readonly struct Segment
        {
            public Segment(string text, string target)
            {
                Text = text;
                Target = target;
            }

            /// <summary>Plain text, or a link's caption (null when it has none).</summary>
            public string Text { get; }

            /// <summary>Null for plain text.</summary>
            public string Target { get; }

            public bool IsLink => Target != null;
        }

        public static List<Segment> Parse(string text)
        {
            var segments = new List<Segment>();
            if (string.IsNullOrEmpty(text)) return segments;

            int position = 0;
            foreach (Match match in LinkPattern.Matches(text))
            {
                if (match.Index > position)
                    segments.Add(new Segment(text.Substring(position, match.Index - position), null));

                string caption = match.Groups[2].Success && match.Groups[2].Value.Trim() != "" ? match.Groups[2].Value.Trim() : null;
                segments.Add(new Segment(caption, match.Groups[1].Value.Trim()));
                position = match.Index + match.Length;
            }

            if (position < text.Length)
                segments.Add(new Segment(text.Substring(position), null));
            return segments;
        }

        public static IEnumerable<string> Targets(string text) =>
            Parse(text).Where(s => s.IsLink).Select(s => s.Target);

        public static string ToPlainText(string text, Func<string, string> titleOf)
        {
            var builder = new StringBuilder();
            foreach (var segment in Parse(text))
                builder.Append(segment.IsLink ? segment.Text ?? titleOf(segment.Target) : segment.Text);
            return builder.ToString();
        }

        public static List<string> Wrap(string text, int maxChars)
        {
            var lines = new List<string>();
            foreach (string paragraphLine in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (string word in paragraphLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }
                    if (line.Length > 0) line.Append(' ');
                    line.Append(word);

                    // Cut words longer than a line.
                    while (line.Length > maxChars)
                    {
                        lines.Add(line.ToString(0, maxChars));
                        line.Remove(0, maxChars);
                    }
                }
                lines.Add(line.ToString());
            }
            return lines;
        }
    }
}
