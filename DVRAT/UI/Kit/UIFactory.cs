using System;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Builds uGUI elements from code, laid out with layout groups.</summary>
    public static class UIFactory
    {
        private static Font _font;

        public static Font Font
        {
            get => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("Arial.ttf"));
            set => _font = value;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform DockTop(RectTransform rt, float height, float offset = 0)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -offset - height);
            rt.offsetMax = new Vector2(0, -offset);
            return rt;
        }

        public static RectTransform DockBottom(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0, height);
            return rt;
        }

        public static Image Fill(Transform parent, Color color, string name = "Fill")
        {
            var rt = Stretch(Rect(name, parent));
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string text, int size = Theme.FontBody, Color? color = null,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text ?? "";
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color ?? Theme.Text;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            label.supportRichText = false;
            return label;
        }

        /// <param name="fixedSize">Width and height in pixels, kept whatever the layout does. Without it the layout sizes the button.</param>
        public static Button Button(Transform parent, string caption, Action onClick, int size = Theme.FontBody,
            Color? textColor = null, Vector2? fixedSize = null)
        {
            Transform host = parent;
            if (fixedSize.HasValue)
            {
                // The layout sizes the cell, the button sits centred in it.
                var cell = Rect("ButtonCell", parent);
                Width(cell, fixedSize.Value.x);
                Height(cell, fixedSize.Value.y);
                host = cell;
            }

            var rt = Rect("Button", host);
            if (fixedSize.HasValue)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = fixedSize.Value;
            }
            var image = rt.gameObject.AddComponent<Image>();

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.pressedColor = Theme.ButtonPressed;
            // No fade, or a key rebuilt on refresh and disabled right after flashes its enabled tint.
            colors.fadeDuration = 0f;
            button.colors = colors;
            ButtonStyles.Apply(button, Theme.ButtonStyle);
            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            var label = Label(rt, caption, size, textColor, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 4, 0, 4, 0);
            return button;
        }

        /// <summary>Children set their own height via <see cref="Height"/>.</summary>
        public static RectTransform Column(Transform parent, float spacing = 4, string name = "Column")
        {
            var rt = Rect(name, parent);
            var group = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return rt;
        }

        /// <summary>Children share the width by their flexible width.</summary>
        public static RectTransform Row(Transform parent, float height = Theme.RowHeight, float spacing = 8, string name = "Row")
        {
            var rt = Rect(name, parent);
            var group = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            Height(rt, height);
            return rt;
        }

        public static T Height<T>(T component, float height) where T : Component
        {
            var element = Element(component);
            element.minHeight = height;
            element.preferredHeight = height;
            // Layout groups report themselves as flexible, so a row would grow into spare space.
            element.flexibleHeight = 0;
            return component;
        }

        /// <summary>Fixed width when <paramref name="flexible"/> is 0, otherwise a share of the rest.</summary>
        public static T Width<T>(T component, float width, float flexible = 0) where T : Component
        {
            var element = Element(component);
            element.preferredWidth = width;
            element.minWidth = flexible > 0 ? 0 : width;
            element.flexibleWidth = flexible;
            return component;
        }

        public static RectTransform Divider(Transform parent)
        {
            var image = Height(Rect("Divider", parent).gameObject.AddComponent<Image>(), Theme.LineWidth);
            image.color = Theme.Line;
            image.raycastTarget = false;
            return image.rectTransform;
        }

        /// <summary>Returns the value label.</summary>
        public static Text KeyValue(Transform parent, string key, string value = "", float keyWidth = 220)
        {
            return Pair(Row(parent), key, value, keyWidth);
        }

        /// <summary>Adds a pair to an existing row, for several side by side.</summary>
        public static Text Pair(Transform row, string key, string value = "", float keyWidth = 150)
        {
            Width(Label(row, key, Theme.FontBody, Theme.TextDim), keyWidth);
            return Width(Label(row, value), 0, 1);
        }

        /// <summary>A width of 0 shares the free width.</summary>
        public static Text Cell(Transform row, string text, float width, Color color, FontStyle style = FontStyle.Normal)
        {
            var label = Label(row, text, Theme.FontSmall, color, TextAnchor.MiddleLeft, style);
            return width > 0f ? Width(label, width) : Width(label, 0, 1);
        }

        public static Text Heading(Transform parent, string text)
        {
            return Height(Label(parent, text, Theme.FontBody, Theme.Highlight, TextAnchor.LowerLeft, FontStyle.Bold), Theme.RowHeight);
        }

        private static LayoutElement Element(Component component)
        {
            var element = component.GetComponent<LayoutElement>();
            return element != null ? element : component.gameObject.AddComponent<LayoutElement>();
        }
    }
}
