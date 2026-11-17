using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Square icon buttons from <see cref="Icons"/>. <see cref="Show"/> dims the ones that would do nothing.</summary>
    public class IconKeys
    {
        public const float Size = 44f;
        private const float IconShare = 26f / 44f;

        private readonly List<(Button button, Image icon, Func<bool> usable)> _keys = new List<(Button, Image, Func<bool>)>();

        /// <summary>Call before building the keys again.</summary>
        public void Clear() => _keys.Clear();

        public Button Add(Transform parent, string icon, Action onClick, Func<bool> usable, float size = Size)
        {
            var button = UIFactory.Button(parent, "", onClick, Theme.FontBody, Theme.Text, new Vector2(size, size));
            var image = UIFactory.Rect("Icon", button.transform).gameObject.AddComponent<Image>();
            image.sprite = Icons.ForLabel(Icons.Label(icon));
            image.color = Theme.Text;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size * IconShare, size * IconShare);
            _keys.Add((button, image, usable));
            return button;
        }

        public void Show()
        {
            foreach (var (button, icon, usable) in _keys)
            {
                if (button == null) continue;
                bool on = usable();
                if (button.interactable != on)
                    button.interactable = on;
                var tint = on ? Theme.Text : Theme.TextDim;
                if (icon.color != tint)
                    icon.color = tint;
            }
        }
    }
}
