using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Plays a Windows program (Spotify, a browser) through the tablet. The program is silent on the PC meanwhile. Windows only.</summary>
    public class PcAudioWidget : IPageWidget, IBackStep
    {
        private const string AppSetting = "pcAudioApp";
        private const float VolumeKeySize = 32f;
        private const float DetailWidth = 110f;

        private MediaPlayer _media;
        private RATTabletController _tablet;
        private PagedList _list;
        private readonly IconKeys _keys = new IconKeys();
        // What the list was last drawn from. Unchanged means no redraw.
        private string _shownKey;
        // Open program list, kept in the view state so every tablet copy agrees. Each player lists the programs on their own PC.
        private const string PickingKey = "picking";
        private Dictionary<string, string> _state = new Dictionary<string, string>();
        private bool Picking
        {
            get => _state.ContainsKey(PickingKey);
            set
            {
                if (value) _state[PickingKey] = "1";
                else _state.Remove(PickingKey);
            }
        }

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _media = context.Tablet != null ? context.Tablet.Media : null;
            _tablet = context.Tablet;
            _state = context.View(pageId) ?? new Dictionary<string, string>();
            _keys.Clear();
            _list = null;
            _shownKey = null;
            CaptureApps.LogLinuxDiagnosticsOnce();
            string unavailable = _media == null ? "PC Audio needs the tablet."
                : !CaptureTrack.Available ? (CaptureApps.UnderWine
                    ? "PC Audio needs capture/ratcapture-linux in the mod folder."
                    : "PC Audio needs capture/ratcapture.exe in the mod folder.")
                : null;
            if (unavailable != null)
            {
                UIFactory.Height(UIFactory.Label(parent, unavailable, Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                return;
            }
            var media = _media;
            CaptureApps.Request();
            if (Picking)
            {
                _list = new PagedList(parent, rows ?? 0, EmptyText(), Theme.RowHeight, _state, "programs");
                ShowList(context);
                return;
            }

            var nowPlaying = UIFactory.Label(parent, "", Theme.FontTitle, Theme.Highlight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(nowPlaying, Theme.FontTitle + 12);
            LiveText.Attach(nowPlaying, () => NowPlaying(media));
            var status = UIFactory.Label(parent, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleCenter);
            UIFactory.Height(status, Theme.FontSmall + 8);
            LiveText.Attach(status, () => Status(media));

            var keys = UIFactory.Row(parent, IconKeys.Size + 4, 10, "PcAudioKeys");
            keys.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Key(keys, Icons.Stop, () => media.Stop(), () => media.CaptureApp != null, context);
            Key(keys, Icons.Previous, () => media.Previous(), () => media.CaptureApp != null && media.CanPrevious, context);
            Key(keys, Icons.Play, () => Play(media), () => CanPlay(media), context);
            Key(keys, Icons.Pause, () => media.Pause(), () => media.CaptureApp != null && media.CanPause, context);
            Key(keys, Icons.Next, () => media.Next(), () => media.CaptureApp != null && media.CanNext, context);

            // Takes the free height so the volume row sits at the bottom.
            if (SpectrumView.IsOn(context))
                SpectrumView.Create(parent, media);
            else
                UIFactory.Rect("Spacer", parent).gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var volume = UIFactory.Row(parent, VolumeKeySize + 4, 10, "Volume");
            volume.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            Key(volume, Icons.Minus, () => media.ChangeVolume(-MediaPlayer.VolumeStep), () => media.Volume > 0, context, VolumeKeySize);
            var barCell = UIFactory.Column(volume, 0, "VolumeBar");
            barCell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Width(barCell, 0, 1);
            ProgressBar.CreateVolume(barCell, () => media.Volume / 100f);
            Key(volume, Icons.Plus, () => media.ChangeVolume(MediaPlayer.VolumeStep), () => media.Volume < 100, context, VolumeKeySize);
            _keys.Show();
        }

        public void Refresh(PageContext context)
        {
            if (_media == null) return;
            if (_list != null)
            {
                CaptureApps.Request();
                ShowList(context);
            }
            _keys.Show();
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            if (_media == null || !CaptureTrack.Available) yield break;
            if (!Picking)
            {
                yield return new SoftKey("Programs", () =>
                {
                    Picking = true;
                    context.Rebuild();
                });
                yield return SpectrumView.Key(context);
            }
            if (_list == null) yield break;
            foreach (var key in _list.SoftKeys())
                yield return key;
        }

        public bool CanGoBack => Picking;

        public void GoBack(PageContext context)
        {
            Picking = false;
            context.Rebuild();
        }

        private void ShowList(PageContext context)
        {
            var apps = CaptureApps.Latest ?? new List<CaptureApp>();
            string playing = _media.CaptureApp?.ProcessName ?? "";
            string key = $"{CaptureApps.Latest != null}|{CaptureApps.Error}|{playing}|" +
                string.Join("|", apps.Select(a => a.ProcessName + a.DisplayName + a.HasMedia));
            if (key == _shownKey) return;
            // The empty text is set at build time, so rebuild to update it.
            if (apps.Count == 0 && _shownKey != null)
            {
                context.Rebuild();
                return;
            }
            _shownKey = key;
            _list.SetGroups(apps.Select(app => (IReadOnlyList<Action<RectTransform>>)new Action<RectTransform>[]
                { row => AppRow(row, context, app, playing) }).ToList());
        }

        private void AppRow(RectTransform row, PageContext context, CaptureApp app, string playing)
        {
            IRowCells cells = new DrawnCells(row);
            cells.Selectable(string.Equals(app.ProcessName, playing, StringComparison.OrdinalIgnoreCase), () =>
            {
                Remember(app);
                _media.PlayCapture(app);
                Picking = false;
                context.Rebuild();
            }, does: "play");
            cells.Label(app.DisplayName);
            cells.Cell(app.HasMedia ? "song and keys" : "sound only", DetailWidth, Theme.TextDim);
        }

        private string EmptyText() =>
            CaptureApps.Error ?? (CaptureApps.Latest == null
                ? "Looking for programs that play sound ..."
                : "No program plays sound right now. Start one, e.g. Spotify, and it shows up here.");

        /// <summary>The program the Play key starts while none plays: the last one, including from an earlier game.</summary>
        private CaptureApp LastApp()
        {
            if (_media.LastCaptureApp != null) return _media.LastCaptureApp;
            string saved = _tablet.Session.Setting(AppSetting);
            int split = saved.IndexOf('|');
            return split > 0 ? new CaptureApp(0, saved.Substring(0, split), saved.Substring(split + 1), false) : null;
        }

        private void Remember(CaptureApp app) => _tablet.Session.Settings[AppSetting] = app.ProcessName + "|" + app.DisplayName;

        private bool CanPlay(MediaPlayer media) =>
            media.CaptureApp != null ? media.CanPlay : media.State == PlayState.Stopped && LastApp() != null;

        private void Play(MediaPlayer media)
        {
            if (media.CaptureApp != null)
                media.PlayOrResume();
            else
                media.PlayCapture(LastApp());
        }

        private static string NowPlaying(MediaPlayer media)
        {
            var app = media.CaptureApp;
            if (app == null) return media.Error ?? "PC Audio off";
            if (media.CaptureWaiting) return $"Waiting for {app.DisplayName} ...";
            string song = string.IsNullOrEmpty(media.CaptureTitle) ? app.DisplayName : media.CaptureTitle;
            return media.State == PlayState.Paused ? song + " (paused)" : song;
        }

        private static string Status(MediaPlayer media) =>
            media.CaptureApp != null ? "Capturing: " + media.CaptureApp.DisplayName : "";

        private void Key(Transform parent, string icon, Action action, Func<bool> usable, PageContext context,
            float size = IconKeys.Size) =>
            _keys.Add(parent, icon, () =>
            {
                action();
                context.Refresh();
            }, usable, size);
    }
}
