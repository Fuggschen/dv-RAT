using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The fixed frame around every page: header, content area and soft key bar.</summary>
    public class PageHost
    {
        /// <summary>Slots right of the Start key, all for the page's own keys.</summary>
        public const int SoftKeyCount = 7;
        public const int PageKeySlots = SoftKeyCount;

        private const float IconSize = 30f;

        // The clock always reads "HH:mm / HH:mm", so the box is as wide as that text.
        private readonly float _clockWidth;

        // Fits the icon and "-30 °C", or "Partly cloudy -30 °C" without the icon.
        private const float WeatherWidth = 220;
        private const float WeatherIconSize = 34f;

        private const float MediaIconSize = 26f;
        private RectTransform _media;
        private Image _mediaIcon;
        // Width the weather takes left of the cellular bar, gap included. 0 while hidden.
        private float _weatherSpace;

        private const float SignalWidth = 34f;
        private const float SignalHeight = 26f;
        private const float SignalBarGap = 3f;
        private const float UnlitAlpha = 0.25f;

        private readonly RectTransform _root;
        private readonly Image[] _signalBars = new Image[TabletRadioSignal.BarCount];
        private int _signalLit = -1;
        private readonly Text _title;
        // The back arrow and its divider line, shown while the title can go back.
        private readonly GameObject _back;
        private readonly Text _clock;
        private readonly RectTransform _weather;
        private readonly Image _weatherIcon;
        private readonly Text _weatherText;
        private readonly RectTransform _softKeyBar;

        // What the bar keys were built from, and the action each runs now.
        private readonly List<(string label, bool half, bool enabled)> _shownKeys = new List<(string, bool, bool)>();
        private readonly List<Action> _keyActions = new List<Action>();
        private bool _keysBuilt;

        /// <param name="onTitleClicked">Also runs for the back arrow.</param>
        /// <param name="logo">Shown on the Start key. Null shows the text "Start".</param>
        public PageHost(RectTransform canvasRoot, Action onClockClicked, Action onWeatherClicked, Action onMediaClicked,
            Action onTitleClicked, Texture logo, Action onStartClicked)
        {
            _root = UIFactory.Stretch(UIFactory.Rect("PageHost", canvasRoot));
            UIFactory.Fill(_root, Theme.Background, "Background");

            var header = UIFactory.DockTop(UIFactory.Rect("Header", _root), Theme.HeaderHeight);
            UIFactory.Fill(header, Theme.Panel);
            _clockWidth = MeasureClock(header);
            _title = UIFactory.Label(header, "", Theme.FontTitle, Theme.HeaderText, TextAnchor.MiddleLeft, FontStyle.Bold);
            // The right inset follows the weather width, see LayOutRightSide.
            UIFactory.Stretch(_title.rectTransform, Theme.Padding, 0, _clockWidth + 10 + SignalWidth + 10, 0);
            _title.raycastTarget = true;
            var titleButton = _title.gameObject.AddComponent<Button>();
            titleButton.targetGraphic = _title;
            titleButton.transition = Selectable.Transition.None;
            titleButton.onClick.AddListener(() => onTitleClicked());

            var back = UIFactory.Rect("Back", header);
            back.anchorMin = new Vector2(0, 0);
            back.anchorMax = new Vector2(0, 1);
            back.pivot = new Vector2(0, 0.5f);
            back.sizeDelta = new Vector2(BackWidth, 0);
            back.anchoredPosition = new Vector2(Theme.Padding, 0);
            var arrow = UIFactory.Label(back, "<", Theme.FontTitle, Theme.HeaderText, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(arrow.rectTransform, 0, 0, 0, 0);
            arrow.raycastTarget = true;
            var backButton = arrow.gameObject.AddComponent<Button>();
            backButton.targetGraphic = arrow;
            backButton.transition = Selectable.Transition.None;
            backButton.onClick.AddListener(() => onTitleClicked());
            var line = UIFactory.Rect("Line", back).gameObject.AddComponent<Image>();
            line.color = Theme.HeaderText;
            line.raycastTarget = false;
            var lineRect = line.rectTransform;
            lineRect.anchorMin = new Vector2(1, 0.2f);
            lineRect.anchorMax = new Vector2(1, 0.8f);
            lineRect.pivot = new Vector2(1, 0.5f);
            lineRect.sizeDelta = new Vector2(Theme.LineWidth, 0);
            _back = back.gameObject;
            _back.SetActive(false);
            _clock = UIFactory.Label(header, "", Theme.FontTitle, Theme.HeaderText, TextAnchor.MiddleRight);
            var clockRect = _clock.rectTransform;
            clockRect.anchorMin = new Vector2(1, 0);
            clockRect.anchorMax = new Vector2(1, 1);
            clockRect.pivot = new Vector2(1, 0.5f);
            clockRect.sizeDelta = new Vector2(_clockWidth, 0);
            clockRect.anchoredPosition = new Vector2(-Theme.Padding, 0);
            _clock.raycastTarget = true;
            var clockButton = _clock.gameObject.AddComponent<Button>();
            clockButton.targetGraphic = _clock;
            clockButton.transition = Selectable.Transition.None;
            clockButton.onClick.AddListener(() => onClockClicked());

            BuildSignal(header);

            // Right-aligned against the cellular bar, so it grows left.
            _weather = UIFactory.Rect("Weather", header);
            _weather.anchorMin = new Vector2(1, 0);
            _weather.anchorMax = new Vector2(1, 1);
            _weather.pivot = new Vector2(1, 0.5f);
            _weather.sizeDelta = new Vector2(WeatherWidth, 0);
            _weather.anchoredPosition = new Vector2(-(Theme.Padding + _clockWidth + 10 + SignalWidth + 10), 0);
            var weatherHit = _weather.gameObject.AddComponent<Image>();
            weatherHit.color = Color.clear;
            var weatherButton = _weather.gameObject.AddComponent<Button>();
            weatherButton.targetGraphic = weatherHit;
            weatherButton.transition = Selectable.Transition.None;
            weatherButton.onClick.AddListener(() => onWeatherClicked());
            var weatherLayout = _weather.gameObject.AddComponent<HorizontalLayoutGroup>();
            weatherLayout.childAlignment = TextAnchor.MiddleRight;
            weatherLayout.spacing = 6;
            weatherLayout.childControlWidth = true;
            weatherLayout.childControlHeight = true;
            weatherLayout.childForceExpandWidth = false;
            weatherLayout.childForceExpandHeight = false;
            _weatherIcon = UIFactory.Rect("Icon", _weather).gameObject.AddComponent<Image>();
            _weatherIcon.preserveAspect = true;
            _weatherIcon.raycastTarget = false;
            var iconSize = _weatherIcon.gameObject.AddComponent<LayoutElement>();
            iconSize.preferredWidth = iconSize.preferredHeight = WeatherIconSize;
            _weatherText = UIFactory.Label(_weather, "", Theme.FontTitle, Theme.HeaderText, TextAnchor.MiddleRight);
            _weather.gameObject.SetActive(false);
            BuildMedia(header, onMediaClicked);
            var headerLine = UIFactory.DockBottom(UIFactory.Rect("Line", header), Theme.LineWidth);
            headerLine.gameObject.AddComponent<Image>().color = Theme.Line;

            // Clips tall pages at the screen edges so shadows and text in the padding stay whole.
            var viewport = UIFactory.Stretch(UIFactory.Rect("Viewport", _root), 0, Theme.HeaderHeight, 0, Theme.SoftKeyHeight);
            viewport.gameObject.AddComponent<RectMask2D>();
            Content = UIFactory.Column(viewport, 4, "Content");
            UIFactory.Stretch(Content, Theme.Padding, Theme.Padding, Theme.Padding, Theme.Padding);

            _softKeyBar = UIFactory.DockBottom(UIFactory.Rect("SoftKeys", _root), Theme.SoftKeyHeight);
            UIFactory.Fill(_softKeyBar, Theme.Panel).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var keys = _softKeyBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            keys.spacing = 4;
            keys.padding = new RectOffset(4, 4, 6, 6);
            keys.childControlWidth = true;
            keys.childControlHeight = true;
            // The Start key keeps its width, every other key and blank shares equally, see Share.
            keys.childForceExpandWidth = false;
            keys.childForceExpandHeight = true;
            StartKey(logo, onStartClicked);
        }

        /// <summary>Widest of the clock texts, since digits differ in width. One pixel of slack stops wrapping.</summary>
        private static float MeasureClock(RectTransform parent)
        {
            var probe = UIFactory.Label(parent, "", Theme.FontTitle, Theme.HeaderText, TextAnchor.MiddleRight);
            float width = 0f;
            foreach (var sample in new[] { "00:00 / 00:00", "88:88 / 88:88" })
            {
                probe.text = sample;
                width = Mathf.Max(width, probe.preferredWidth);
            }
            probe.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(probe.gameObject);
            return Mathf.Ceil(width) + 1f;
        }

        /// <summary>The leftmost key, as wide as it is high. Soft keys are rebuilt right of it.</summary>
        private void StartKey(Texture logo, Action onClick)
        {
            float size = Theme.SoftKeyHeight - 12;
            var button = UIFactory.Width(UIFactory.Button(_softKeyBar, logo != null ? "" : "Start", onClick, Theme.FontSmall), size);
            button.name = StartKeyName;
            if (logo == null) return;
            var image = UIFactory.Rect("Logo", button.transform).gameObject.AddComponent<RawImage>();
            image.texture = logo;
            image.raycastTarget = false;
            UIFactory.Stretch(image.rectTransform, 4, 4, 4, 4);
        }

        private const string StartKeyName = "Start";

        private static T Share<T>(T component) where T : Component => UIFactory.Width(component, 0, 1);

        public RectTransform Content { get; }

        public RectTransform Root => _root;

        public bool Visible
        {
            get => _root.gameObject.activeSelf;
            set => _root.gameObject.SetActive(value);
        }

        /// <param name="canGoBack">Shows the back arrow left of the title.</param>
        public void SetTitle(string title, bool canGoBack)
        {
            _title.text = title;
            if (_back.activeSelf == canGoBack) return;
            _back.SetActive(canGoBack);
            var rect = _title.rectTransform;
            rect.offsetMin = new Vector2(Theme.Padding + (canGoBack ? BackWidth + 12 : 0), rect.offsetMin.y);
        }

        /// <summary>The arrow and its line. The title starts 12 px after the line.</summary>
        private const float BackWidth = 26f;

        public void SetClock(string text) => _clock.text = text;

        private void BuildSignal(RectTransform header)
        {
            var signal = UIFactory.Rect("Signal", header);
            signal.anchorMin = new Vector2(1, 0.5f);
            signal.anchorMax = new Vector2(1, 0.5f);
            signal.pivot = new Vector2(1, 0.5f);
            signal.sizeDelta = new Vector2(SignalWidth, SignalHeight);
            signal.anchoredPosition = new Vector2(-(Theme.Padding + _clockWidth + 10), 0);

            float barWidth = (SignalWidth - SignalBarGap * (_signalBars.Length - 1)) / _signalBars.Length;
            for (int i = 0; i < _signalBars.Length; i++)
            {
                var bar = UIFactory.Rect("Bar" + (i + 1), signal).gameObject.AddComponent<Image>();
                bar.raycastTarget = false;
                var rect = bar.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 0);
                rect.sizeDelta = new Vector2(barWidth, SignalHeight * (i + 1) / _signalBars.Length);
                rect.anchoredPosition = new Vector2(i * (barWidth + SignalBarGap), 0);
                _signalBars[i] = bar;
            }
            SetSignal(0);
        }

        /// <param name="bars">Lit bars, 0 to <see cref="TabletRadioSignal.BarCount"/>.</param>
        public void SetSignal(int bars)
        {
            if (bars == _signalLit) return;
            _signalLit = bars;
            for (int i = 0; i < _signalBars.Length; i++)
            {
                var color = Theme.HeaderText;
                if (i >= bars)
                    color.a *= UnlitAlpha;
                _signalBars[i].color = color;
            }
        }

        /// <summary>Hidden when there is neither icon nor text.</summary>
        public void SetWeather(Sprite icon, string text)
        {
            bool show = icon != null || !string.IsNullOrEmpty(text);
            if (_weather.gameObject.activeSelf != show)
                _weather.gameObject.SetActive(show);
            if (show)
            {
                if (_weatherIcon.gameObject.activeSelf != (icon != null))
                    _weatherIcon.gameObject.SetActive(icon != null);
                _weatherIcon.sprite = icon;
                _weatherText.text = text ?? "";
            }

            // The layout's width lags one frame behind a new text.
            _weatherSpace = show ? Mathf.Min(WeatherWidth, LayoutUtility.GetPreferredWidth(_weather)) + 10 : 0f;
            LayOutRightSide();
        }

        /// <summary>The media player's play or pause symbol. Hidden when null.</summary>
        public void SetMedia(Sprite icon)
        {
            bool show = icon != null;
            if (_media.gameObject.activeSelf != show)
                _media.gameObject.SetActive(show);
            if (show && _mediaIcon.sprite != icon)
                _mediaIcon.sprite = icon;
            LayOutRightSide();
        }

        private void BuildMedia(RectTransform header, Action onClicked)
        {
            _media = UIFactory.Rect("Media", header);
            _media.anchorMin = new Vector2(1, 0);
            _media.anchorMax = new Vector2(1, 1);
            _media.pivot = new Vector2(1, 0.5f);
            _media.sizeDelta = new Vector2(MediaIconSize, 0);
            var hit = _media.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var button = _media.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => onClicked());
            _mediaIcon = UIFactory.Rect("Icon", _media).gameObject.AddComponent<Image>();
            _mediaIcon.color = Theme.HeaderText;
            _mediaIcon.raycastTarget = false;
            var icon = _mediaIcon.rectTransform;
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.sizeDelta = new Vector2(MediaIconSize, MediaIconSize);
            _media.gameObject.SetActive(false);
        }

        /// <summary>Puts the media symbol left of the weather. The title gets the remaining room.</summary>
        private void LayOutRightSide()
        {
            float right = _clockWidth + 10 + SignalWidth + 10 + _weatherSpace;
            float mediaX = -(Theme.Padding + right);
            if (!Mathf.Approximately(_media.anchoredPosition.x, mediaX))
                _media.anchoredPosition = new Vector2(mediaX, 0);
            if (_media.gameObject.activeSelf)
                right += MediaIconSize + 10;

            var title = _title.rectTransform;
            if (!Mathf.Approximately(title.offsetMax.x, -right))
                title.offsetMax = new Vector2(-right, title.offsetMax.y);
        }

        public void ClearContent()
        {
            for (int i = Content.childCount - 1; i >= 0; i--)
            {
                var child = Content.GetChild(i).gameObject;
                // Destroy runs at frame end, so deactivate first to keep the child out of the new layout.
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        /// <summary>Fills the bar left to right, padding with blanks. Keys that look unchanged only get the new actions.</summary>
        public void SetSoftKeys(IEnumerable<SoftKey> softKeys)
        {
            var keys = Arrange(softKeys, out int rightStart);
            if (SameLook(keys))
            {
                for (int i = 0; i < keys.Count; i++)
                    _keyActions[i] = keys[i].Action;
                return;
            }

            _keysBuilt = true;
            _shownKeys.Clear();
            _keyActions.Clear();
            foreach (var key in keys)
            {
                _shownKeys.Add((key.Label, key.Half, key.Action != null));
                _keyActions.Add(key.Action);
            }

            for (int i = _softKeyBar.childCount - 1; i >= 0; i--)
            {
                var child = _softKeyBar.GetChild(i);
                if (child.name == "Fill" || child.name == StartKeyName) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            int used = 0;
            // The slot of a half key that has room for a second one.
            RectTransform split = null;
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                // A left half key never shares a slot with a right key.
                if (i == rightStart)
                    split = null;
                if (key.Half && split != null)
                {
                    Key(split, key, i);
                    split = null;
                    continue;
                }
                split = null;
                if (used == SoftKeyCount) break;
                used++;
                if (key.Label == null)
                    Blank();
                else if (key.Half)
                    Key(split = SplitSlot(), key, i);
                else
                    Key(_softKeyBar, key, i);
            }

            for (; used < SoftKeyCount; used++)
                Blank();
        }

        /// <summary>Orders the keys for the bar: <see cref="SoftKey.Right"/> keys go last, left keys that leave no room are dropped.</summary>
        /// <param name="rightStart">Index of the first right key, or the list count when there is none.</param>
        private static List<SoftKey> Arrange(IEnumerable<SoftKey> softKeys, out int rightStart)
        {
            var left = new List<SoftKey>();
            var right = new List<SoftKey>();
            foreach (var key in softKeys)
                (key.Right ? right : left).Add(key);
            rightStart = left.Count;
            if (right.Count == 0) return left;

            int rightSlots = SoftKeySlots.Count(right, IsHalf);
            int fitting = SoftKeySlots.Fitting(left, IsHalf, Math.Max(0, SoftKeyCount - rightSlots));
            var keys = left.GetRange(0, fitting);
            int blanks = Math.Max(0, SoftKeyCount - SoftKeySlots.Count(keys, IsHalf) - rightSlots);
            for (int i = 0; i < blanks; i++)
                keys.Add(SoftKey.Blank);
            rightStart = keys.Count;
            keys.AddRange(right);
            return keys;
        }

        private static bool IsHalf(SoftKey key) => key.Half;

        private bool SameLook(List<SoftKey> keys)
        {
            if (!_keysBuilt || keys.Count != _shownKeys.Count) return false;
            for (int i = 0; i < keys.Count; i++)
            {
                var (label, half, enabled) = _shownKeys[i];
                if (keys[i].Label != label || keys[i].Half != half || (keys[i].Action != null) != enabled) return false;
            }
            return true;
        }

        /// <param name="index">The key's place in <see cref="_keyActions"/>.</param>
        private void Key(Transform parent, SoftKey key, int index)
        {
            Action action = key.Action != null ? () => _keyActions[index]?.Invoke() : (Action)null;
            var icon = Icons.ForLabel(key.Label);
            var button = Share(UIFactory.Button(parent, icon != null ? "" : key.Label, action, Theme.FontSmall,
                action != null ? Theme.Text : Theme.TextDim));
            button.interactable = action != null;
            if (icon == null) return;
            var image = UIFactory.Rect("Icon", button.transform).gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.color = action != null ? Theme.Text : Theme.TextDim;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
        }

        /// <summary>One slot that two half keys share.</summary>
        private RectTransform SplitSlot()
        {
            var slot = UIFactory.Rect("Split", _softKeyBar);
            // A layout group reports its children's flexible width (two), which would give the slot two shares.
            Share(slot);
            var keys = slot.gameObject.AddComponent<HorizontalLayoutGroup>();
            keys.spacing = 2;
            keys.childControlWidth = true;
            keys.childControlHeight = true;
            keys.childForceExpandWidth = true;
            keys.childForceExpandHeight = true;
            return slot;
        }

        private void Blank()
        {
            var blank = Share(UIFactory.Rect("Blank", _softKeyBar).gameObject.AddComponent<Image>());
            blank.color = Color.Lerp(Theme.ButtonFill, Theme.Background, 0.5f);
            blank.raycastTarget = false;
            ButtonStyles.ApplyBlank(blank, Theme.ButtonStyle);
        }
    }
}
