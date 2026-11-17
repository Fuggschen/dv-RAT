using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DVRAT
{
    /// <summary>The URL is a stream or a playlist.</summary>
    public class RadioStation
    {
        public RadioStation(string name, string url)
        {
            Name = name;
            Url = url;
        }

        public string Name { get; }
        public string Url { get; }
    }

    /// <summary>
    /// Splits an "Icy-MetaData: 1" stream into audio and titles. After every <c>metaInt</c> audio bytes comes one length
    /// byte (times 16), then that many bytes of metadata. Pushes may cut blocks anywhere.
    /// </summary>
    public class IcyStream
    {
        private readonly int _metaInt;
        private readonly Action<byte[], int, int> _audio;
        private readonly Action<string> _title;
        private int _untilMeta;
        private int _metaRemaining;
        private readonly MemoryStream _meta = new MemoryStream();

        /// <param name="metaInt">The "icy-metaint" header. 0 or less when the station sends no titles.</param>
        public IcyStream(int metaInt, Action<byte[], int, int> audio, Action<string> title)
        {
            _metaInt = metaInt;
            _audio = audio;
            _title = title;
            _untilMeta = metaInt;
        }

        public void Push(byte[] data, int count)
        {
            if (_metaInt <= 0)
            {
                if (count > 0) _audio(data, 0, count);
                return;
            }

            int i = 0;
            while (i < count)
            {
                if (_metaRemaining > 0)
                {
                    int n = Math.Min(_metaRemaining, count - i);
                    _meta.Write(data, i, n);
                    i += n;
                    _metaRemaining -= n;
                    if (_metaRemaining == 0)
                    {
                        string title = StreamTitle(_meta.ToArray());
                        if (title != null) _title(title);
                        _untilMeta = _metaInt;
                    }
                }
                else if (_untilMeta == 0)
                {
                    _metaRemaining = data[i++] * 16;
                    _meta.SetLength(0);
                    if (_metaRemaining == 0)
                        _untilMeta = _metaInt;
                }
                else
                {
                    int n = Math.Min(_untilMeta, count - i);
                    _audio(data, i, n);
                    i += n;
                    _untilMeta -= n;
                }
            }
        }

        private static readonly Regex TitlePattern =
            new Regex(@"StreamTitle='(.*?)';(?=\s*(?:Stream\w*=|\0|$))", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>Null without a title. Stations send UTF-8 or Latin-1.</summary>
        public static string StreamTitle(byte[] block)
        {
            string text = Encoding.UTF8.GetString(block);
            // Latin-1 by hand, since Unity's Mono may lack the codepage tables.
            if (text.IndexOf('\uFFFD') >= 0)
            {
                var latin1 = new char[block.Length];
                for (int i = 0; i < block.Length; i++)
                    latin1[i] = (char)block[i];
                text = new string(latin1);
            }
            var match = TitlePattern.Match(text.TrimEnd('\0'));
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }

    public static class RadioPlaylist
    {
        private static readonly string[] PlaylistEndings = { ".pls", ".m3u", ".m3u8" };

        public static bool IsPlaylistUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            string path = uri.AbsolutePath.ToLowerInvariant();
            foreach (string ending in PlaylistEndings)
                if (path.EndsWith(ending)) return true;
            return false;
        }

        /// <summary>The first stream URL of a .pls or .m3u. Null when there is none or for HLS (#EXT-X-), which ffmpeg cannot play here.</summary>
        public static string FirstStream(string playlist)
        {
            if (string.IsNullOrEmpty(playlist) || playlist.Contains("#EXT-X-")) return null;
            var urls = new List<string>();
            foreach (string raw in playlist.Split('\n'))
            {
                string line = raw.Trim();
                int equals = line.IndexOf('=');
                if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && equals > 0)
                    line = line.Substring(equals + 1).Trim();
                if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    urls.Add(line);
            }
            return urls.Count > 0 ? urls[0] : null;
        }

        /// <summary>Lets ffmpeg skip probing. Null means probe.</summary>
        public static string FfmpegFormat(string contentType)
        {
            string type = (contentType ?? "").ToLowerInvariant();
            if (type.Contains("mpeg") || type.Contains("mp3")) return "mp3";
            if (type.Contains("aac")) return "aac";
            if (type.Contains("ogg") || type.Contains("opus")) return "ogg";
            return null;
        }
    }
}
