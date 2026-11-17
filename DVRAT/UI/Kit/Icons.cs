using System;
using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Symbols drawn in code for keys the tablet font lacks. Soft key labels <c>icon:name</c> select them. White, so buttons can tint them.</summary>
    public static class Icons
    {
        public const string Prefix = "icon:";

        /// <summary>Up is farther, Down is nearer, ToBottom is back to the train.</summary>
        public const string Up = "up", Down = "down", ToBottom = "tobottom";

        public const string Play = "play", Pause = "pause", Stop = "stop", Previous = "previous", Next = "next",
            Shuffle = "shuffle", Folder = "folder", Minus = "minus", Plus = "plus";

        private const int Size = 64;
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static string Label(string name) => Prefix + name;

        /// <summary>Null when the label is not "icon:name".</summary>
        public static Sprite ForLabel(string label)
        {
            if (label == null || !label.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            string name = label.Substring(Prefix.Length);
            if (Cache.TryGetValue(name, out var sprite)) return sprite;

            var pixels = new Color32[Size * Size];
            switch (name)
            {
                case Up:
                    Head(pixels, 62, 44);
                    Head(pixels, 44, 26);
                    Rect(pixels, 27, 4, 37, 30);
                    break;
                case Down:
                    Head(pixels, 2, 20);
                    Head(pixels, 20, 38);
                    Rect(pixels, 27, 34, 37, 60);
                    break;
                case ToBottom:
                    Head(pixels, 14, 38);
                    Rect(pixels, 27, 34, 37, 62);
                    Rect(pixels, 10, 2, 54, 9);
                    break;
                case Play:
                    Triangle(pixels, 14, 6, 14, 58, 56, 32);
                    break;
                case Pause:
                    Rect(pixels, 12, 8, 26, 56);
                    Rect(pixels, 38, 8, 52, 56);
                    break;
                case Stop:
                    Rect(pixels, 10, 10, 54, 54);
                    break;
                case Previous:
                    Rect(pixels, 8, 10, 16, 54);
                    Triangle(pixels, 56, 10, 56, 54, 18, 32);
                    break;
                case Next:
                    Triangle(pixels, 8, 10, 8, 54, 46, 32);
                    Rect(pixels, 48, 10, 56, 54);
                    break;
                case Shuffle:
                    Line(pixels, 6, 14, 40, 50, 4f);
                    Line(pixels, 6, 50, 40, 14, 4f);
                    Triangle(pixels, 38, 40, 38, 60, 58, 50);
                    Triangle(pixels, 38, 4, 38, 24, 58, 14);
                    break;
                case Folder:
                    Rect(pixels, 4, 44, 26, 52);
                    Rect(pixels, 4, 10, 60, 44);
                    break;
                case Minus:
                    Rect(pixels, 10, 27, 54, 37);
                    break;
                case Plus:
                    Rect(pixels, 10, 27, 54, 37);
                    Rect(pixels, 27, 10, 37, 54);
                    break;
                default:
                    return null;
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            Cache[name] = sprite;
            return sprite;
        }

        private static void Head(Color32[] pixels, int tip, int baseRow)
        {
            int rows = Mathf.Abs(baseRow - tip);
            for (int i = 0; i <= rows; i++)
            {
                int y = tip + (baseRow > tip ? i : -i);
                int half = 24 * i / rows;
                Rect(pixels, 32 - half, y, 32 + half, y);
            }
        }

        /// <summary>y counts up from the bottom row.</summary>
        private static void Triangle(Color32[] pixels, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                    float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= 0 && w1 >= 0 && w2 >= 0)
                        pixels[y * Size + x] = new Color32(255, 255, 255, 255);
                }
        }

        private static void Line(Color32[] pixels, float x0, float y0, float x1, float y1, float width)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float lengthSquared = dx * dx + dy * dy;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float t = Mathf.Clamp01(((x - x0) * dx + (y - y0) * dy) / lengthSquared);
                    float ex = x0 + t * dx - x, ey = y0 + t * dy - y;
                    if (ex * ex + ey * ey <= width * width / 4f)
                        pixels[y * Size + x] = new Color32(255, 255, 255, 255);
                }
        }

        private static void Rect(Color32[] pixels, int x0, int y0, int x1, int y1)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(Size - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Size - 1, x1); x++)
                    pixels[y * Size + x] = new Color32(255, 255, 255, 255);
        }
    }
}
