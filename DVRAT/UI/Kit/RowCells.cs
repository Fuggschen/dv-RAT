using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>
    /// Parts of a list row, left to right. <see cref="PagedList"/> runs the description to draw the row and to make its key.
    /// Actions are invisible to the key, so describe what one does beyond the shown text in <c>does</c>.
    /// </summary>
    public interface IRowCells
    {
        /// <param name="width">0 or less takes a <paramref name="flex"/> share of the remaining width.</param>
        void Label(string text, float width = 0, float flex = 1, int size = Theme.FontBody, Color? color = null,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal);

        /// <summary>Empty space that keeps columns aligned.</summary>
        void Gap(float width);

        /// <summary>Without a sprite the space stays empty.</summary>
        void Icon(Sprite sprite, float size);

        /// <param name="width">As for <see cref="Label"/>; ignored with <paramref name="fixedSize"/>.</param>
        void Button(string caption, Action onClick, float width = 0, int size = Theme.FontBody, Color? color = null,
            bool enabled = true, Vector2? fixedSize = null, string does = null);

        void Clickable(Action onClick, string does = null);

        /// <summary>Drawn pressed while <paramref name="selected"/>.</summary>
        void Selectable(bool selected, Action onClick, string does = null);

        /// <summary><paramref name="key"/> must hold everything <paramref name="draw"/> shows.</summary>
        void Custom(string key, Action<RectTransform> draw);
    }

    public static class RowCellsExtensions
    {
        /// <summary>Like <see cref="UIFactory.Cell"/>.</summary>
        public static void Cell(this IRowCells cells, string text, float width, Color color, FontStyle style = FontStyle.Normal) =>
            cells.Label(text, width, size: Theme.FontSmall, color: color, style: style);
    }

    internal sealed class DrawnCells : IRowCells
    {
        private readonly RectTransform _row;

        public DrawnCells(RectTransform row) => _row = row;

        public void Label(string text, float width, float flex, int size, Color? color, TextAnchor anchor, FontStyle style) =>
            Size(UIFactory.Label(_row, text, size, color, anchor, style), width, flex);

        public void Gap(float width) => UIFactory.Width(UIFactory.Rect("Gap", _row), width);

        public void Icon(Sprite sprite, float size)
        {
            var icon = UIFactory.Rect("Icon", _row).gameObject.AddComponent<Image>();
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = sprite != null;
            UIFactory.Width(icon, size);
        }

        public void Button(string caption, Action onClick, float width, int size, Color? color, bool enabled, Vector2? fixedSize,
            string does)
        {
            var button = UIFactory.Button(_row, caption, onClick, size, color, fixedSize);
            button.interactable = enabled;
            if (!fixedSize.HasValue)
                Size(button, width, 1);
        }

        public void Clickable(Action onClick, string does) => RowButton(Color.clear, onClick);

        public void Selectable(bool selected, Action onClick, string does) =>
            RowButton(selected ? Theme.ButtonPressed : Color.clear, onClick);

        public void Custom(string key, Action<RectTransform> draw) => draw(_row);

        private void RowButton(Color color, Action onClick)
        {
            var image = _row.gameObject.AddComponent<Image>();
            image.color = color;
            var button = _row.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.onClick.AddListener(() => onClick());
        }

        private static void Size(Component part, float width, float flex)
        {
            if (width > 0f)
                UIFactory.Width(part, width);
            else
                UIFactory.Width(part, 0, flex);
        }
    }

    internal sealed class KeyCells : IRowCells
    {
        private readonly StringBuilder _key = new StringBuilder();

        public string Key => _key.ToString();

        public void Label(string text, float width, float flex, int size, Color? color, TextAnchor anchor, FontStyle style) =>
            Add("L", text, width, flex, size, color, anchor, style);

        public void Gap(float width) => Add("G", width);

        public void Icon(Sprite sprite, float size) => Add("I", sprite != null ? sprite.name : null, size);

        public void Button(string caption, Action onClick, float width, int size, Color? color, bool enabled, Vector2? fixedSize,
            string does) => Add("B", caption, width, size, color, enabled, fixedSize, does);

        public void Clickable(Action onClick, string does) => Add("C", does);

        public void Selectable(bool selected, Action onClick, string does) => Add("S", selected, does);

        public void Custom(string key, Action<RectTransform> draw) => Add("X", key);

        private void Add(params object[] values)
        {
            foreach (var value in values)
                _key.Append(value).Append('\u001f');
            _key.Append('\u001e');
        }
    }
}
