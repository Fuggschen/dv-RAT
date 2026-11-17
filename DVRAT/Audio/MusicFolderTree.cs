using System;
using System.Collections.Generic;
using System.IO;

namespace DVRAT
{
    /// <summary>Scanned music files arranged by folder. Only folders with music below them are listed, in scan order.</summary>
    public class MusicFolderTree
    {
        private static readonly IReadOnlyList<string> None = new string[0];

        private readonly Dictionary<string, List<string>> _subfolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="files">Full paths below the root. Others are left out.</param>
        public MusicFolderTree(string root, IEnumerable<string> files)
        {
            Root = Normalize(root);
            var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string folder = Normalize(Path.GetDirectoryName(file));
                if (!IsInside(folder)) continue;
                Add(_files, folder, file);

                // Link up to the root, stopping at the first folder already linked.
                while (!string.Equals(folder, Root, StringComparison.OrdinalIgnoreCase))
                {
                    if (!linked.Add(folder)) break;
                    string parent = Normalize(Path.GetDirectoryName(folder));
                    Add(_subfolders, parent, folder);
                    folder = parent;
                }
            }
        }

        public string Root { get; }

        public IReadOnlyList<string> Folders(string folder) =>
            _subfolders.TryGetValue(Normalize(folder), out var list) ? list : None;

        /// <summary>Direct children only.</summary>
        public IReadOnlyList<string> Files(string folder) =>
            _files.TryGetValue(Normalize(folder), out var list) ? list : None;

        /// <summary>Null for the root and for folders outside it.</summary>
        public string Parent(string folder)
        {
            string normalized = Normalize(folder);
            if (!IsInside(normalized) || string.Equals(normalized, Root, StringComparison.OrdinalIgnoreCase)) return null;
            return Normalize(Path.GetDirectoryName(normalized));
        }

        public bool Contains(string folder) => !string.IsNullOrEmpty(folder) && IsInside(Normalize(folder));

        private bool IsInside(string folder)
        {
            if (folder.Length == 0 || Root.Length == 0) return false;
            if (string.Equals(folder, Root, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = EndsWithSeparator(Root) ? Root : Root + Path.DirectorySeparatorChar;
            return folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWithSeparator(string path) =>
            path.EndsWith(Path.DirectorySeparatorChar.ToString()) || path.EndsWith(Path.AltDirectorySeparatorChar.ToString());

        private static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<string>();
            list.Add(value);
        }

        /// <summary>Drive roots keep their separator, since "D:" alone means the current folder on drive D.</summary>
        private static string Normalize(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            string full = Path.GetFullPath(folder);
            string root = Path.GetPathRoot(full);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return full;
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
