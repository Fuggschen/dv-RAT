using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Spectrum of the player pages, in <see cref="Theme.Meter"/> colors like the volume bars. Follows what the speakers play, not what the track has read ahead.</summary>
    public class SpectrumView : MonoBehaviour
    {
        /// <summary>Tablet setting that swaps the list for the spectrum.</summary>
        public const string Setting = "spectrum";

        private const int Bars = 16;
        private const int Segments = 12;
        // Faster only costs frames.
        private const float UpdateSeconds = 1f / 30f;

        private MediaPlayer _media;
        private SpectrumAnalyzer _analyzer;
        private readonly float[] _window = new float[SpectrumAnalyzer.Size];
        private readonly float[] _target = new float[Bars];
        private readonly BarMotion _motion = new BarMotion(Bars);
        private Image[,] _segments;
        private Color[] _lit;
        private Color _unlit;
        private float _nextUpdate;
        private float _lastUpdate;

        public static bool IsOn(PageContext context) => context.Tablet != null && context.Tablet.Session.Setting(Setting) == "on";

        public static SoftKey Key(PageContext context) => new SoftKey(IsOn(context) ? "List" : "Spectrum", () =>
        {
            if (context.Tablet == null) return;
            context.Tablet.Session.Settings[Setting] = IsOn(context) ? "off" : "on";
            context.Rebuild();
        });

        public static SpectrumView Create(Transform parent, MediaPlayer media)
        {
            var root = UIFactory.Rect("Spectrum", parent);
            var element = root.gameObject.AddComponent<LayoutElement>();
            element.minHeight = 60f;
            element.flexibleHeight = 1f;
            root.gameObject.AddComponent<Image>().color = Theme.Meter.Frame;

            var bars = UIFactory.Stretch(UIFactory.Rect("Bars", root), 6, 6, 6, 6);
            var row = bars.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 4;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            var view = root.gameObject.AddComponent<SpectrumView>();
            view._media = media;
            view._analyzer = new SpectrumAnalyzer(AudioSettings.outputSampleRate, Bars);
            view._unlit = Theme.Meter.Off;
            view._lit = new Color[Segments];
            for (int s = 0; s < Segments; s++)
            {
                float top = (s + 1f) / Segments;
                view._lit[s] = top > ProgressBar.TooLoudFrom ? Theme.Meter.Red
                    : top > ProgressBar.LoudFrom ? Theme.Meter.Yellow
                    : Theme.Meter.Green;
            }
            view._segments = new Image[Bars, Segments];
            for (int b = 0; b < Bars; b++)
            {
                var bar = UIFactory.Rect("Bar", bars);
                var column = bar.gameObject.AddComponent<VerticalLayoutGroup>();
                column.spacing = 2;
                column.childControlWidth = column.childControlHeight = true;
                column.childForceExpandWidth = column.childForceExpandHeight = true;
                // The layout fills from the top.
                for (int s = Segments - 1; s >= 0; s--)
                {
                    var segment = UIFactory.Rect("Segment", bar).gameObject.AddComponent<Image>();
                    segment.color = view._unlit;
                    view._segments[b, s] = segment;
                }
            }
            return view;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUpdate) return;
            float elapsed = _lastUpdate > 0f ? Time.unscaledTime - _lastUpdate : UpdateSeconds;
            _lastUpdate = Time.unscaledTime;
            _nextUpdate = Time.unscaledTime + UpdateSeconds;

            if (_media != null && _media.SpectrumWindow(_window))
                _analyzer.Analyze(_window, _target);
            else
                System.Array.Clear(_target, 0, Bars);
            _motion.Step(_target, elapsed);

            for (int b = 0; b < Bars; b++)
            {
                int lit = Mathf.RoundToInt(_motion.Levels[b] * Segments);
                for (int s = 0; s < Segments; s++)
                {
                    var color = s < lit ? _lit[s] : _unlit;
                    var image = _segments[b, s];
                    if (image.color != color) image.color = color;
                }
            }
        }
    }
}
