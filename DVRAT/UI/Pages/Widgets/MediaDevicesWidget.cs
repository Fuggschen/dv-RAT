using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The speakers and subwoofer the tablet plays through, each with a volume and an EQ view. A strong EQ boost overdrives the device on purpose.</summary>
    public class MediaDevicesWidget : IPageWidget, IBackStep
    {
        private const float RowHeight = 44f;
        private const float StepKeySize = 32f;
        private const float BarWidth = 120f;
        private const float EqLabelWidth = 90f;
        private const float EqStepDb = 2f;

        private MediaPlayer _media;
        private PagedList _list;
        private readonly IconKeys _keys = new IconKeys();
        // The devices the list was built for.
        private List<RATSpeaker> _shown = new List<RATSpeaker>();
        // ID of the device whose EQ is open, kept in the view state so every tablet copy shows the same.
        private const string EqKey = "eq";
        private Dictionary<string, string> _state = new Dictionary<string, string>();
        private RATSpeaker _eqDevice;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _media = context.Tablet != null ? context.Tablet.Media : null;
            _list = null;
            _keys.Clear();
            if (_media == null)
            {
                UIFactory.Height(UIFactory.Label(parent, "The devices need the tablet.", Theme.FontBody, Theme.TextDim),
                    Theme.RowHeight);
                return;
            }

            _state = context.View(pageId) ?? new Dictionary<string, string>();
            _shown = _media.Devices.ToList();
            // A removed device shows the list. Its EQ reopens if it returns.
            _eqDevice = _state.TryGetValue(EqKey, out string eqId) ? _shown.FirstOrDefault(d => d.Settings.Id == eqId) : null;
            if (_eqDevice != null)
                BuildEq(parent, context, _eqDevice);
            else
                BuildList(parent, context, rows);
            _keys.Show();
        }

        public void Refresh(PageContext context)
        {
            if (_media == null) return;
            // A device was mounted or removed, or the train was split.
            if (!_media.Devices.SequenceEqual(_shown))
            {
                context.Rebuild();
                return;
            }
            _keys.Show();
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context) =>
            _list != null ? _list.SoftKeys() : Enumerable.Empty<SoftKey>();

        public bool CanGoBack => _eqDevice != null;

        public void GoBack(PageContext context)
        {
            _state.Remove(EqKey);
            context.Rebuild();
        }

        private void BuildList(RectTransform parent, PageContext context, int? rows)
        {
            string empty = FfmpegTrack.Available
                ? "No speakers connected. Mount speakers or a subwoofer on the train with this tablet; they connect within a few seconds."
                : "Speakers need ffmpeg.exe in the mod's ffmpeg folder; without it only the tablet plays.";
            _list = new PagedList(parent, rows ?? 0, empty, RowHeight, _state, "devices");
            _list.SetGroups(_shown.Select(device => (IReadOnlyList<Action<RectTransform>>)new Action<RectTransform>[]
            {
                row => DeviceRow(row, context, device),
            }));
        }

        private void DeviceRow(RectTransform row, PageContext context, RATSpeaker device)
        {
            UIFactory.Width(UIFactory.Label(row, Name(device), Theme.FontBody, Theme.Text), 0, 1);
            var settings = device.Settings;
            Step(row, Icons.Minus, () => settings.Volume -= MediaPlayer.VolumeStep, () => settings.Volume > 0, context);
            Bar(row, () => settings.Volume / 100f, BarWidth, volume: true);
            Step(row, Icons.Plus, () => settings.Volume += MediaPlayer.VolumeStep, () => settings.Volume < 100, context);
            UIFactory.Button(row, "EQ", () =>
            {
                _state[EqKey] = device.Settings.Id;
                context.Rebuild();
            }, Theme.FontSmall, Theme.Text, new Vector2(60, StepKeySize));
        }

        private void BuildEq(RectTransform parent, PageContext context, RATSpeaker device)
        {
            UIFactory.Height(UIFactory.Label(parent, Name(device) + ": EQ", Theme.FontTitle, Theme.Highlight,
                TextAnchor.MiddleCenter, FontStyle.Bold), Theme.FontTitle + 12);
            UIFactory.Divider(parent);
            var settings = device.Settings;
            EqRow(parent, context, "Lows", () => settings.LowsDb, db => settings.LowsDb = db);
            if (!SpeakerSettings.HasMidsAndHighs(device.Kind)) return;
            EqRow(parent, context, "Mids", () => settings.MidsDb, db => settings.MidsDb = db);
            EqRow(parent, context, "Highs", () => settings.HighsDb, db => settings.HighsDb = db);
        }

        private void EqRow(Transform parent, PageContext context, string label, Func<float> get, Action<float> set)
        {
            var row = UIFactory.Row(parent, RowHeight, 10, label);
            UIFactory.Width(UIFactory.Label(row, label, Theme.FontBody, Theme.Text), EqLabelWidth);
            float max = ToneSettings.MaxEqDb;
            Step(row, Icons.Minus, () => set(get() - EqStepDb), () => get() > -max, context);
            // Full is +12 dB, empty is -12 dB.
            Bar(row, () => (get() + max) / (2 * max), 0);
            Step(row, Icons.Plus, () => set(get() + EqStepDb), () => get() < max, context);
        }

        private void Step(Transform row, string icon, Action change, Func<bool> usable, PageContext context) =>
            _keys.Add(row, icon, () =>
            {
                change();
                context.Refresh();
            }, usable, StepKeySize);

        /// <param name="width">Fixed width. 0 takes the remaining room.</param>
        private static void Bar(Transform row, Func<float> value, float width, bool volume = false)
        {
            var cell = UIFactory.Column(row, 0, "Bar");
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Width(cell, width, width > 0 ? 0 : 1);
            if (volume) ProgressBar.CreateVolume(cell, value);
            else ProgressBar.Create(cell, value);
        }

        /// <summary>E.g. "Speaker 2 on 123-456". Speakers are numbered in the order the tablet found them.</summary>
        private string Name(RATSpeaker device)
        {
            string kind;
            if (device.Kind == SpeakerKind.Subwoofer)
                kind = "Subwoofer";
            else
                kind = "Speaker " + (_shown.Where(d => d.Kind == SpeakerKind.Speaker).ToList().IndexOf(device) + 1);
            var car = device.TrainCar;
            return car != null ? $"{kind} on {car.ID}" : kind;
        }
    }
}
