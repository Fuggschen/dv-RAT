using System;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>Button shapes picked with the <c>buttons</c> setting. The button's own image is the fill, Outline and Windows 2000 add an edge image on top.</summary>
    public static class ButtonStyles
    {
        public const string Simple = "simple";
        public const string Win2000 = "win2000";
        public const string Round = "round";
        public const string Outline = "outline";
        public const string Pill = "pill";

        /// <summary>Texels per canvas unit. Set to the canvas's reference pixels per unit, so one edge texel is one unit of the 800 x 520 screen.</summary>
        public static float PixelsPerUnit = 100f;

        private const int RoundRadius = 8;
        // PillShape draws it at half the button height. The texture only sets curve smoothness.
        internal const int PillRadius = 32;
        private const int EdgeWidth = 2;
        private const string EdgeName = "Edge";

        private static Sprite _round;
        private static Sprite _pill;
        private static Sprite _frame;
        private static Sprite _raised;
        private static Sprite _sunken;

        public static void Apply(Button button, string style)
        {
            var image = (Image)button.targetGraphic;
            switch (style)
            {
                case Round:
                    Shape(image, RoundSprite());
                    break;
                case Pill:
                    MakePill(image);
                    break;
                case Outline:
                    AddEdge(image.transform, FrameSprite(), Theme.Highlight);
                    break;
                case Win2000:
                    AddEdge(image.transform, BevelSprite(sunken: false), Color.white);
                    break;
            }
            SetSelected(button, style, false);
        }

        /// <summary>Pressed fill, plus the sunken edge for Windows 2000. An Outline button has no fill until chosen.</summary>
        public static void SetSelected(Button button, string style, bool selected)
        {
            var image = (Image)button.targetGraphic;
            image.color = selected ? Theme.ButtonPressed : style == Outline ? Color.clear : Theme.ButtonFill;
            var edge = style == Win2000 ? EdgeOf(button) : null;
            if (edge != null)
                edge.sprite = BevelSprite(selected);
        }

        /// <summary>Shows the button pressed during a tap. The returned action restores the look. Buttons without a pressed look, like the header clock, stay as they are.</summary>
        public static Action Press(Button button, string style)
        {
            if (button.transition == Selectable.Transition.None || !(button.targetGraphic is Image image))
                return () => { };
            var color = image.color;
            image.color = color == Theme.ButtonPressed ? Color.Lerp(Theme.ButtonPressed, Theme.Text, 0.2f) : Theme.ButtonPressed;
            var edge = style == Win2000 ? EdgeOf(button) : null;
            var sprite = edge != null ? edge.sprite : null;
            if (edge != null)
                edge.sprite = BevelSprite(sunken: true);
            return () =>
            {
                if (image != null)
                    image.color = color;
                if (edge != null)
                    edge.sprite = sprite;
            };
        }

        private static Image EdgeOf(Button button)
        {
            var edge = button.transform.Find(EdgeName);
            return edge != null ? edge.GetComponent<Image>() : null;
        }

        /// <summary>Empty soft key slot. Shows nothing for Outline.</summary>
        public static void ApplyBlank(Image image, string style)
        {
            switch (style)
            {
                case Round:
                    Shape(image, RoundSprite());
                    break;
                case Pill:
                    MakePill(image);
                    break;
                case Outline:
                    image.color = Color.clear;
                    break;
            }
        }

        private static void Shape(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        private static void MakePill(Image image)
        {
            Shape(image, PillSprite());
            image.gameObject.AddComponent<PillShape>();
        }

        private static void AddEdge(Transform button, Sprite sprite, Color color)
        {
            var edge = UIFactory.Stretch(UIFactory.Rect(EdgeName, button)).gameObject.AddComponent<Image>();
            edge.sprite = sprite;
            edge.type = Image.Type.Sliced;
            edge.fillCenter = false;
            edge.color = color;
            edge.raycastTarget = false;
        }

        // Runtime textures unload with a scene, so a cached sprite can turn null.
        private static Sprite RoundSprite() => _round != null ? _round : _round = RoundedSprite(RoundRadius, 0);
        private static Sprite PillSprite() => _pill != null ? _pill : _pill = RoundedSprite(PillRadius, 0);
        private static Sprite FrameSprite() => _frame != null ? _frame : _frame = RoundedSprite(0, EdgeWidth);

        private static Sprite BevelSprite(bool sunken)
        {
            if (sunken)
                return _sunken != null ? _sunken : _sunken = Bevel(true);
            return _raised != null ? _raised : _raised = Bevel(false);
        }

        /// <summary>White rounded rectangle, filled or only an edge <paramref name="line"/> texels wide. Tinted by the button color.</summary>
        private static Sprite RoundedSprite(int radius, int line)
        {
            int border = Mathf.Max(radius, line);
            int size = 2 * border + 2;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float dx = Mathf.Max(Mathf.Max(radius - px, px - (size - radius)), 0f);
                    float dy = Mathf.Max(Mathf.Max(radius - py, py - (size - radius)), 0f);
                    float inside = dx > 0f && dy > 0f
                        ? radius - Mathf.Sqrt(dx * dx + dy * dy)
                        : Mathf.Min(Mathf.Min(px, size - px), Mathf.Min(py, size - py));
                    float alpha = Mathf.Clamp01(inside + 0.5f);
                    if (line > 0)
                        alpha *= 1f - Mathf.Clamp01(inside - line + 0.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            return MakeSprite(pixels, size, border, FilterMode.Bilinear);
        }

        /// <summary>Two-texel Windows 2000 edge, light top and left and dark bottom and right. Swapped when sunken.</summary>
        private static Sprite Bevel(bool sunken)
        {
            const int size = 2 * EdgeWidth + 1;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // y counts from the bottom.
                    int ring = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                    if (ring >= EdgeWidth)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }
                    bool lowerRight = x >= size - 1 - ring || y <= ring;
                    float alpha = ring == 0 ? 0.9f : 0.4f;
                    pixels[y * size + x] = lowerRight != sunken ? new Color(0f, 0f, 0f, alpha) : new Color(1f, 1f, 1f, alpha);
                }
            }
            return MakeSprite(pixels, size, EdgeWidth, FilterMode.Point);
        }

        private static Sprite MakeSprite(Color[] pixels, int size, int border, FilterMode filter)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = filter,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }

    /// <summary>Keeps pill ends round. A sliced image shrinks its corners per axis, which flattens them on a low button, so this scales the sprite instead.</summary>
    public class PillShape : MonoBehaviour
    {
        private Image _image;

        private void OnEnable() => Fit();

        private void OnRectTransformDimensionsChange() => Fit();

        private void Fit()
        {
            if (_image == null)
                _image = GetComponent<Image>();
            float height = ((RectTransform)transform).rect.height;
            // Zero until the layout has run.
            if (_image == null || height <= 0f) return;
            _image.pixelsPerUnitMultiplier = 2f * ButtonStyles.PillRadius / height;
        }
    }
}
