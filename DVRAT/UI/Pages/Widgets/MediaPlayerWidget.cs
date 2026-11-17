using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The player keys and music folder browser. Playback runs in the tablet's <see cref="MediaPlayer"/>, so it continues on other pages.</summary>
    public class MediaPlayerWidget : IPageWidget, IBackStep
    {
        private const float VolumeKeySize = 32f;
        // Room for "1:02:05" in the small font.
        private const float TimeWidth = 64f;
        private const float FolderIconSize = 22f;

        private PagedList _list;
        private Button _shuffleKey;
        private readonly IconKeys _keys = new IconKeys();
        private MediaPlayer _media;
        /// <summary>Page variable for the open folder. Never synced, since each multiplayer player browses their own music folder.</summary>
        private const string FolderVar = "mediaFolder";
        private Dictionary<string, string> _vars;

        // Null before the first scan.
        private string OpenFolder
        {
            get => _vars != null && _vars.TryGetValue(FolderVar, out string folder) && folder.Length > 0 ? folder : null;
            set
            {
                if (_vars != null)
                    _vars[FolderVar] = value ?? "";
            }
        }
        // What the list was last built from. Describing thousands of songs on each refresh took about 60 ms.
        private MusicFolderTree _builtLibrary;
        private MusicFolderTree _shownLibrary;
        private string _shownFolder;
        private string _shownFile;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _media = context.Tablet != null ? context.Tablet.Media : null;
            if (_media == null)
            {
                UIFactory.Height(UIFactory.Label(parent, "The media player needs the tablet.", Theme.FontBody, Theme.TextDim),
                    Theme.RowHeight);
                return;
            }
            _vars = context.Tablet.Session.Vars(pageId, null);
            var media = _media;
            if (media.Library == null)
                media.Rescan();
            var library = media.Library;
            _builtLibrary = library;
            if (library != null && !library.Contains(OpenFolder))
                OpenFolder = library.Root;

            var nowPlaying = UIFactory.Label(parent, "", Theme.FontTitle, Theme.Highlight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(nowPlaying, Theme.FontTitle + 12);
            LiveText.Attach(nowPlaying, () => NowPlaying(media));

            var progress = UIFactory.Row(parent, Theme.RowHeight, 10, "Progress");
            var played = UIFactory.Width(UIFactory.Label(progress, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), TimeWidth);
            LiveText.Attach(played, () => media.State != PlayState.Stopped ? Format.PlayTime(media.Position) : "");
            var songBar = UIFactory.Column(progress, 0, "SongBar");
            songBar.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Width(songBar, 0, 1);
            ProgressBar.Create(songBar, () => media.Duration > 0f ? media.Position / media.Duration : 0f, 8f);
            var length = UIFactory.Width(UIFactory.Label(progress, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleLeft), TimeWidth);
            LiveText.Attach(length, () =>
                media.State == PlayState.Stopped ? ""
                : media.Station != null || media.CaptureApp != null ? "LIVE"
                : media.Duration > 0f ? Format.PlayTime(media.Duration)
                : "--:--");

            var keys = UIFactory.Row(parent, IconKeys.Size + 4, 10, "PlayerKeys");
            keys.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            _keys.Clear();
            Key(keys, Icons.Stop, () => media.Stop(), () => media.CanStop, context);
            Key(keys, Icons.Previous, () => media.Previous(), () => media.CanPrevious, context);
            Key(keys, Icons.Play, () => media.PlayOrResume(), () => media.CanPlay, context);
            Key(keys, Icons.Next, () => media.Next(), () => media.CanNext, context);
            Key(keys, Icons.Pause, () => media.Pause(), () => media.CanPause, context);
            _shuffleKey = Key(keys, Icons.Shuffle, () => media.SetShuffle(!media.Shuffle), () => true, context);

            if (!media.ExtraFormats && library != null)
                UIFactory.Height(UIFactory.Label(parent, "ffmpeg.exe is missing: only mp3, ogg and wav are listed.",
                    Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter), Theme.FontSmall + 6);
            UIFactory.Divider(parent);

            _list = null;
            if (SpectrumView.IsOn(context))
                SpectrumView.Create(parent, media);
            else if (library != null)
            {
                var folderRow = UIFactory.Row(parent, Theme.RowHeight, 8, "Folder");
                UIFactory.Width(UIFactory.Label(folderRow, FolderTitle(library), Theme.FontBody, Theme.Text,
                    TextAnchor.MiddleLeft, FontStyle.Bold), 0, 1);
                var songs = library.Files(OpenFolder);
                if (songs.Count > 0)
                    UIFactory.Button(folderRow, "Play all", () =>
                    {
                        media.Play(songs, 0);
                        context.Refresh();
                    }, Theme.FontSmall, Theme.Text, new Vector2(100, Theme.RowHeight - 6));
            }

            if (!SpectrumView.IsOn(context))
                _list = new PagedList(parent, rows ?? 0, $"No music files in {MediaPlayer.Folder}.", Theme.RowHeight,
                    context.View(pageId), "songs", OpenFolder);

            // Built before the list is filled, or the list counts one row too many that fits.
            var volume = UIFactory.Row(parent, VolumeKeySize + 4, 10, "Volume");
            volume.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Key(volume, Icons.Minus, () => media.ChangeVolume(-MediaPlayer.VolumeStep), () => media.Volume > 0, context, VolumeKeySize);
            var barCell = UIFactory.Column(volume, 0, "VolumeBar");
            barCell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Width(barCell, 0, 1);
            ProgressBar.CreateVolume(barCell, () => media.Volume / 100f);
            Key(volume, Icons.Plus, () => media.ChangeVolume(MediaPlayer.VolumeStep), () => media.Volume < 100, context, VolumeKeySize);
            _shownLibrary = null;
            if (_list != null)
                ShowFolder(context);
            ShowKeys();
        }

        public void Refresh(PageContext context)
        {
            if (_media == null) return;
            // A finished scan changes the folder row too.
            if (_list != null && _media.Library != _builtLibrary)
            {
                context.Rebuild();
                return;
            }
            ShowKeys();
            if (_list != null)
                ShowFolder(context);
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            if (_media == null) yield break;
            var media = _media;

            yield return new SoftKey("Rescan", !media.Scanning ? () =>
            {
                media.Rescan();
                context.Refresh();
            } : (Action)null);
            yield return SpectrumView.Key(context);
            if (_list != null)
                foreach (var key in _list.SoftKeys())
                    yield return key;
        }

        public bool CanGoBack => _media?.Library != null && OpenFolder != null && _media.Library.Parent(OpenFolder) != null;

        public void GoBack(PageContext context)
        {
            OpenFolder = _media.Library.Parent(OpenFolder);
            context.Rebuild();
        }

        private void ShowFolder(PageContext context)
        {
            var library = _media.Library;
            // Before the first scan the list's empty text would wrongly claim the folder is empty.
            if (library == null) return;
            string playing = _media.CurrentFile;
            if (library == _shownLibrary && OpenFolder == _shownFolder && playing == _shownFile) return;
            _shownLibrary = library;
            _shownFolder = OpenFolder;
            _shownFile = playing;

            // Groups draw only the rows on the shown screen, when this is called.
            var rows = new List<IReadOnlyList<Action<RectTransform>>>();
            // The way up is the first row because the Back key is far from the songs.
            string parent = library.Parent(OpenFolder);
            if (parent != null)
                rows.Add(new Action<RectTransform>[] { row => UpRow(row, context, parent) });
            foreach (string folder in library.Folders(OpenFolder))
                rows.Add(new Action<RectTransform>[] { row => FolderRow(row, context, folder) });
            var songs = library.Files(OpenFolder);
            for (int i = 0; i < songs.Count; i++)
            {
                int index = i;
                rows.Add(new Action<RectTransform>[] { row => SongRow(row, context, songs, index, playing) });
            }
            _list.SetGroups(rows);
        }

        private void UpRow(RectTransform row, PageContext context, string parent)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Clickable(() =>
            {
                OpenFolder = parent;
                context.Rebuild();
            }, does: "open");
            cells.Icon(Icons.ForLabel(Icons.Label(Icons.Folder)), FolderIconSize);
            cells.Label("../");
        }

        private void FolderRow(RectTransform row, PageContext context, string folder)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Clickable(() =>
            {
                OpenFolder = folder;
                context.Rebuild();
            }, does: "open");
            cells.Icon(Icons.ForLabel(Icons.Label(Icons.Folder)), FolderIconSize);
            cells.Label(Path.GetFileName(folder));
        }

        private void SongRow(RectTransform row, PageContext context, IReadOnlyList<string> songs, int index, string playing)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Selectable(songs[index] == playing, () =>
            {
                _media.Play(songs, index);
                context.Refresh();
            }, does: "play");
            cells.Gap(FolderIconSize);
            cells.Label(Path.GetFileNameWithoutExtension(songs[index]));
        }

        /// <summary>A square player key, dimmed while <paramref name="usable"/> is false.</summary>
        private Button Key(Transform parent, string icon, Action action, Func<bool> usable, PageContext context,
            float size = IconKeys.Size) =>
            _keys.Add(parent, icon, () =>
            {
                action();
                context.Refresh();
            }, usable, size);

        /// <summary>Dims keys that would do nothing and holds the shuffle key down while shuffle is on.</summary>
        private void ShowKeys()
        {
            _keys.Show();
            if (_shuffleKey == null) return;
            var colors = _shuffleKey.colors;
            var normal = _media.Shuffle ? Theme.ButtonPressed : Color.white;
            if (colors.normalColor == normal) return;
            colors.normalColor = normal;
            colors.highlightedColor = normal;
            colors.selectedColor = normal;
            _shuffleKey.colors = colors;
        }

        private string FolderTitle(MusicFolderTree library)
        {
            if (OpenFolder == library.Root) return Path.GetFileName(library.Root) is string name && name.Length > 0 ? name : library.Root;
            // The path below the music folder, so "CD1" shows its album.
            return OpenFolder.Substring(library.Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar.ToString(), " > ");
        }

        private static string NowPlaying(MediaPlayer media)
        {
            if (media.Error != null) return media.Error;
            if (media.Station != null)
                return RadioLine(media);
            if (media.CaptureApp != null)
                return CaptureLine(media);
            if (media.CurrentFile != null)
            {
                string name = Path.GetFileNameWithoutExtension(media.CurrentFile);
                return media.State == PlayState.Paused ? name + " (paused)" : name;
            }
            if (media.Library == null || media.Scanning) return $"Reading {MediaPlayer.Folder} ...";
            return "Nothing playing";
        }

        /// <summary>E.g. "Spotify: Artist - Title". The program alone while it waits or sends no title.</summary>
        public static string CaptureLine(MediaPlayer media)
        {
            string app = media.CaptureApp.DisplayName;
            if (media.CaptureWaiting) return app + ": waiting ...";
            string line = string.IsNullOrEmpty(media.CaptureTitle) ? app : $"{app}: {media.CaptureTitle}";
            return media.State == PlayState.Paused ? line + " (paused)" : line;
        }

        /// <summary>E.g. "TruckersFM: Artist - Title". The station alone while it connects or sends no title.</summary>
        public static string RadioLine(MediaPlayer media)
        {
            string station = media.Station.Name;
            if (media.RadioConnecting) return station + ": connecting ...";
            if (media.RadioBuffering) return station + ": buffering ...";
            return string.IsNullOrEmpty(media.RadioTitle) ? station : $"{station}: {media.RadioTitle}";
        }
    }
}
