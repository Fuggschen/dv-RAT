using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Internet radio with tabs for own stations, directory genres and countries. Plays through the tablet's <see cref="MediaPlayer"/>, so speakers and volume are shared.</summary>
    public class RadioWidget : IPageWidget, IBackStep
    {
        private enum Tab { Mine, Genres, Countries }

        private const string TabVar = "radioTab";
        private const float VolumeKeySize = 32f;
        private const float RowButtonWidth = 72f;

        private MediaPlayer _media;
        private RATTabletController _tablet;
        private string _pageId;
        private Tab _tab = Tab.Mine;
        private PagedList _list;
        private readonly IconKeys _keys = new IconKeys();

        // The open genre or country, kept in the view state so every tablet copy shows the same. Null shows the category list.
        private const string CategoryIdKey = "categoryId", CategoryNameKey = "categoryName", CategoryCountKey = "categoryStations";
        private Dictionary<string, string> _state = new Dictionary<string, string>();
        private DirectoryCategory _category;
        private DirectoryQuery<List<DirectoryCategory>> _categories;
        private DirectoryQuery<List<DirectoryStation>> _stations;
        // The list the playing station was picked from, for the previous and next keys.
        private List<RadioStation> _tunedList = new List<RadioStation>();
        // What the list was last drawn from. Unchanged means no redraw.
        private string _shownKey;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _pageId = pageId;
            _state = context.View(pageId) ?? new Dictionary<string, string>();
            OpenSavedCategory();
            _media = context.Tablet != null ? context.Tablet.Media : null;
            _keys.Clear();
            _list = null;
            _shownKey = null;
            if (_media == null)
            {
                UIFactory.Height(UIFactory.Label(parent, "The radio needs the tablet.", Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                return;
            }
            _tablet = context.Tablet;
            if (context.Tablet.Session.Vars(pageId, null).TryGetValue(TabVar, out string saved) && Enum.TryParse(saved, out Tab tab))
                _tab = tab;
            if (_tab == Tab.Mine)
                RadioStations.Reload();
            var media = _media;

            var nowPlaying = UIFactory.Label(parent, "", Theme.FontTitle, Theme.Highlight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(nowPlaying, Theme.FontTitle + 12);
            LiveText.Attach(nowPlaying, () => NowPlaying(media));
            var status = UIFactory.Label(parent, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Height(status, Theme.FontSmall + 8);
            LiveText.Attach(status, () => Status(media));

            var keys = UIFactory.Row(parent, IconKeys.Size + 4, 10, "RadioKeys");
            keys.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Key(keys, Icons.Stop, () => media.Stop(), () => media.Station != null, context);
            Key(keys, Icons.Previous, () => Tune(-1), () => _tunedList.Count > 1, context);
            Key(keys, Icons.Play, () => media.PlayRadio(LastStation()), () => media.Station == null && LastStation() != null, context);
            Key(keys, Icons.Next, () => Tune(+1), () => _tunedList.Count > 1, context);

            var tabs = UIFactory.Row(parent, 34, 8, "Tabs");
            TabButton(tabs, context, Tab.Mine, "My stations");
            TabButton(tabs, context, Tab.Genres, "Genres");
            TabButton(tabs, context, Tab.Countries, "Countries");
            if (_category != null)
                UIFactory.Height(UIFactory.Label(parent, _category.Name, Theme.FontBody, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold),
                    Theme.RowHeight);
            UIFactory.Divider(parent);

            if (SpectrumView.IsOn(context))
                SpectrumView.Create(parent, media);
            else
                _list = new PagedList(parent, rows ?? 0, EmptyText(), Theme.RowHeight, _state, "stations", _tab + "|" + _category?.Id);

            // Built before the list is filled, since the list counts the rows that fit.
            var volume = UIFactory.Row(parent, VolumeKeySize + 4, 10, "Volume");
            volume.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Key(volume, Icons.Minus, () => media.ChangeVolume(-MediaPlayer.VolumeStep), () => media.Volume > 0, context, VolumeKeySize);
            var barCell = UIFactory.Column(volume, 0, "VolumeBar");
            barCell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Width(barCell, 0, 1);
            ProgressBar.CreateVolume(barCell, () => media.Volume / 100f);
            Key(volume, Icons.Plus, () => media.ChangeVolume(MediaPlayer.VolumeStep), () => media.Volume < 100, context, VolumeKeySize);
            if (_list != null)
            {
                Ask();
                ShowList(context);
            }
            _keys.Show();
        }

        public void Refresh(PageContext context)
        {
            if (_media == null) return;
            if (_list != null)
                ShowList(context);
            _keys.Show();
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            // The page never asks the directory again by itself, only on this key.
            if (DirectoryError != null)
                yield return new SoftKey("Retry", () =>
                {
                    RadioDirectory.Forget(_tab == Tab.Genres ? "genres" : "countries", _category);
                    if (_category != null) _stations = null;
                    else _categories = null;
                    context.Rebuild();
                });
            if (_media != null)
                yield return SpectrumView.Key(context);
            if (_list == null) yield break;
            foreach (var key in _list.SoftKeys())
                yield return key;
        }

        public bool CanGoBack => _category != null;

        public void GoBack(PageContext context)
        {
            SetCategory(null);
            context.Rebuild();
        }

        private void SetCategory(DirectoryCategory category)
        {
            _category = category;
            _stations = null;
            if (category == null)
            {
                _state.Remove(CategoryIdKey);
                _state.Remove(CategoryNameKey);
                _state.Remove(CategoryCountKey);
                return;
            }
            _state[CategoryIdKey] = category.Id;
            _state[CategoryNameKey] = category.Name;
            _state[CategoryCountKey] = category.Stations.ToString();
        }

        // Stations are requested again when the saved category is not the one open here.
        private void OpenSavedCategory()
        {
            if (!_state.TryGetValue(CategoryIdKey, out string id))
            {
                if (_category != null) _stations = null;
                _category = null;
                return;
            }
            if (_category?.Id == id) return;
            _state.TryGetValue(CategoryNameKey, out string name);
            int.TryParse(_state.TryGetValue(CategoryCountKey, out string count) ? count : "0", out int stations);
            _category = new DirectoryCategory(name ?? id, id, stations);
            _stations = null;
        }

        /// <summary>Starts the directory request the open tab needs. Cached answers return at once.</summary>
        private void Ask()
        {
            if (_tab == Tab.Mine) return;
            if (_category != null && _stations == null)
                _stations = _tab == Tab.Genres
                    ? RadioDirectory.StationsByGenre(_category.Id)
                    : RadioDirectory.StationsByCountry(_category.Id);
            else if (_category == null && _categories == null)
                _categories = _tab == Tab.Genres ? RadioDirectory.Genres() : RadioDirectory.Countries();
        }

        /// <summary>Error of the directory answer the open list waits for, or null.</summary>
        private string DirectoryError => _tab == Tab.Mine ? null : _category != null ? _stations?.Error : _categories?.Error;

        private void ShowList(PageContext context)
        {
            string playing = _media.Station?.Url ?? "";
            var rows = new List<IReadOnlyList<Action<RectTransform>>>();
            string key;
            if (_tab == Tab.Mine)
            {
                var stations = RadioStations.Game.Concat(RadioStations.Mine).ToList();
                key = $"mine|{playing}|{string.Join("|", stations.Select(s => s.Url))}";
                foreach (var station in stations)
                    rows.Add(Row(row => StationRow(row, context, station, stations, playing, removable: RadioStations.IsMine(station))));
            }
            else if (_category != null)
            {
                var stations = _stations?.Result ?? new List<DirectoryStation>();
                key = $"stations|{_category.Id}|{_stations?.Done}|{playing}|{stations.Count(RadioStations.IsMine)}";
                var list = stations.Cast<RadioStation>().ToList();
                foreach (var station in stations)
                    rows.Add(Row(row => StationRow(row, context, station, list, playing, removable: false)));
            }
            else
            {
                var categories = _categories?.Result ?? new List<DirectoryCategory>();
                key = $"categories|{_tab}|{_categories?.Done}";
                foreach (var category in categories)
                    rows.Add(Row(row => CategoryRow(row, context, category)));
            }
            if (key == _shownKey) return;
            // The empty text reports directory progress and is set at build time.
            if (rows.Count == 0 && _shownKey != null && !key.StartsWith("mine"))
            {
                context.Rebuild();
                return;
            }
            _shownKey = key;
            _list.SetGroups(rows);
        }

        private static IReadOnlyList<Action<RectTransform>> Row(Action<RectTransform> draw) => new[] { draw };

        private void StationRow(RectTransform row, PageContext context, RadioStation station, List<RadioStation> list,
            string playing, bool removable)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Selectable(station.Url == playing, () =>
            {
                _tunedList = list;
                _media.PlayRadio(station);
                context.Refresh();
            }, does: "tune");
            cells.Label(station.Name);
            if (station is DirectoryStation listed)
            {
                cells.Cell(Details(listed), 90, Theme.TextDim);
                bool saved = RadioStations.IsMine(listed);
                cells.Button(saved ? "Saved" : "Save", () =>
                {
                    RadioStations.AddToMine(new RadioStation(listed.Name, listed.Url));
                    context.Refresh();
                }, RowButtonWidth, Theme.FontSmall, Theme.Text, enabled: !saved, fixedSize: null, does: "save");
            }
            else if (removable)
            {
                cells.Button("Remove", () =>
                {
                    RadioStations.RemoveFromMine(station);
                    // A copy, since the list may be the one the page draws from.
                    _tunedList = _tunedList.Where(s => s.Url != station.Url).ToList();
                    context.Refresh();
                }, RowButtonWidth, Theme.FontSmall, Theme.Text, enabled: true, fixedSize: null, does: "remove");
            }
        }

        private void CategoryRow(RectTransform row, PageContext context, DirectoryCategory category)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Clickable(() =>
            {
                SetCategory(category);
                context.Rebuild();
            }, does: "open");
            cells.Icon(Icons.ForLabel(Icons.Label(Icons.Folder)), 22);
            cells.Label(category.Name);
            cells.Cell(Format.Count(category.Stations, "station"), 110, Theme.TextDim);
        }

        private void TabButton(RectTransform row, PageContext context, Tab tab, string caption)
        {
            var button = UIFactory.Button(row, caption, () =>
            {
                _tab = tab;
                SetCategory(null);
                _categories = null;
                if (context.Tablet != null)
                    context.Tablet.Session.Vars(_pageId, null)[TabVar] = tab.ToString();
                context.Rebuild();
            }, Theme.FontSmall, _tab == tab ? Theme.Highlight : Theme.TextDim);
            button.interactable = _tab != tab;
            UIFactory.Width(button, 0, 1);
        }

        private void Key(Transform parent, string icon, Action action, Func<bool> usable, PageContext context,
            float size = IconKeys.Size) =>
            _keys.Add(parent, icon, () =>
            {
                action();
                context.Refresh();
            }, usable, size);

        /// <summary>Tunes the previous or next station in the list the playing one came from.</summary>
        private void Tune(int step)
        {
            if (_tunedList.Count == 0) return;
            string playing = (_media.Station ?? LastStation())?.Url;
            int index = _tunedList.FindIndex(s => s.Url == playing);
            index = ((index < 0 ? 0 : index + step) % _tunedList.Count + _tunedList.Count) % _tunedList.Count;
            _media.PlayRadio(_tunedList[index]);
        }

        private RadioStation LastStation() => _media.LastStation;

        private string EmptyText()
        {
            if (_tab == Tab.Mine)
                return $"No stations. Add your own in {RadioStations.FolderName}/{RadioStations.FileName} in the mod folder.";
            var query = _category != null ? (object)_stations : _categories;
            string error = _category != null ? _stations?.Error : _categories?.Error;
            bool done = _category != null ? _stations?.Done == true : _categories?.Done == true;
            if (query == null || !done) return "Asking the radio directory ...";
            return error ?? (_category != null ? "No stations this tablet can play." : "The directory sent nothing.");
        }

        private static string Details(DirectoryStation station) =>
            station.Bitrate > 0 ? $"{station.Codec} {station.Bitrate}" : station.Codec;

        private static string NowPlaying(MediaPlayer media)
        {
            if (media.Error != null && media.Station == null) return media.Error;
            if (media.Station != null) return MediaPlayerWidget.RadioLine(media);
            return "Radio off";
        }

        private string Status(MediaPlayer media)
        {
            if (media.Station == null) return media.CurrentFile != null ? "The media player is playing a song" : "";
            int bars = _tablet != null && _tablet.RadioSignal != null ? _tablet.RadioSignal.Bars : 0;
            return bars == 0 ? "No signal" : $"Signal {bars}/{TabletRadioSignal.BarCount}";
        }
    }
}
