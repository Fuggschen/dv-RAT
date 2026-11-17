using System;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Reads its value every frame, for work that outlasts a data refresh.</summary>
    public class ProgressBar : MonoBehaviour
    {
        /// <summary>From here a volume bar shows <see cref="Theme.Meter.Yellow"/>.</summary>
        public const float LoudFrom = 0.6f;
        /// <summary>From here a volume bar shows <see cref="Theme.Meter.Red"/>.</summary>
        public const float TooLoudFrom = 0.9f;

        private Func<float> _value;
        private RectTransform _fill;
        private Image _fillImage;
        private bool _volume;

        public static ProgressBar Create(Transform parent, Func<float> value, float height = 14f)
        {
            var frame = UIFactory.Height(UIFactory.Rect("ProgressBar", parent), height);
            frame.gameObject.AddComponent<Image>().color = Theme.Panel;
            var fill = UIFactory.Stretch(UIFactory.Rect("Fill", frame), 2, 2, 2, 2);
            var image = fill.gameObject.AddComponent<Image>();
            image.color = Theme.Text;

            var bar = frame.gameObject.AddComponent<ProgressBar>();
            bar._value = value;
            bar._fill = fill;
            bar._fillImage = image;
            bar.Update();
            return bar;
        }

        /// <summary>Volume bar in <see cref="Theme.Meter"/> colors, yellow from 60 % and red from 90 %.</summary>
        public static ProgressBar CreateVolume(Transform parent, Func<float> value, float height = 14f)
        {
            var bar = Create(parent, value, height);
            bar.GetComponent<Image>().color = Theme.Meter.Frame;
            bar._volume = true;
            bar.Update();
            return bar;
        }

        private void Update()
        {
            float value = Mathf.Clamp01(_value());
            _fill.anchorMax = new Vector2(value, 1);
            if (!_volume) return;
            // Slightly below the thresholds so float sums of 0.1 steps still reach 60 % and 90 %.
            var color = value >= TooLoudFrom - 0.001f ? Theme.Meter.Red
                : value >= LoudFrom - 0.001f ? Theme.Meter.Yellow
                : Theme.Meter.Normal;
            if (_fillImage.color != color)
                _fillImage.color = color;
        }
    }
}
