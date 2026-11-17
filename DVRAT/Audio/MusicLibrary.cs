using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace DVRAT
{
    /// <summary>Finds music files under a folder. No Unity types, so it can run on a worker thread.</summary>
    public static class MusicLibrary
    {
        /// <summary>Ends junction loops.</summary>
        public const int MaxDepth = 32;

        /// <summary>Extensions are ".mp3" style, any case. Reparse points are followed, as a Music folder moved to OneDrive is one.</summary>
        public static List<string> Scan(string root, ICollection<string> extensions)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return files;

            var wanted = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new Stack<(string path, int depth)>();
            folders.Push((root, 0));
            while (folders.Count > 0)
            {
                var (folder, depth) = folders.Pop();
                string[] found;
                string[] subfolders;
                try
                {
                    if (!visited.Add(Path.GetFullPath(folder))) continue;
                    found = Directory.GetFiles(folder);
                    subfolders = depth < MaxDepth ? Directory.GetDirectories(folder) : new string[0];
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is SecurityException)
                {
                    continue;
                }

                foreach (string file in found)
                    if (wanted.Contains(Path.GetExtension(file)))
                        files.Add(file);
                for (int i = subfolders.Length - 1; i >= 0; i--)
                    folders.Push((subfolders[i], depth + 1));
            }
            return files;
        }
    }
}
