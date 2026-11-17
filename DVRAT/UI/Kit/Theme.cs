using System;
using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Colors and sizes for every screen. Each tablet has its own theme, so its <see cref="RATUIManager"/> sets <see cref="Current"/> before building or updating.</summary>
    public static class Theme
    {
        public class Palette
        {
            public Color Background;
            public Color Panel;
            /// <summary>Drawn on <see cref="Panel"/>.</summary>
            public Color HeaderText;
            public Color Text;
            public Color TextDim;
            public Color Highlight;
            public Color Warning;
            public Color Done;
            /// <summary>Second highlight, e.g. other locos on the yard tracks.</summary>
            public Color Accent;
            public Color ButtonFill;
            public Color ButtonPressed;
            public Color Line;
        }

        public const string DefaultPalette = "light";

        /// <summary>Keyed by the value of the <c>theme</c> setting.</summary>
        public static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>(StringComparer.OrdinalIgnoreCase)
        {
            // Like the EbuLa display in DB cabs.
            ["light"] = new Palette
            {
                Background = new Color(0.86f, 0.86f, 0.82f),
                Panel = new Color(0.74f, 0.74f, 0.70f),
                HeaderText = Color.black,
                Text = new Color(0.05f, 0.05f, 0.05f),
                TextDim = new Color(0.3f, 0.3f, 0.3f),
                Highlight = Color.black,
                Warning = new Color(0.62f, 0.05f, 0.05f),
                Done = new Color(0.05f, 0.42f, 0.1f),
                Accent = new Color(0.1f, 0.3f, 0.7f),
                ButtonFill = new Color(0.78f, 0.78f, 0.74f),
                ButtonPressed = new Color(0.6f, 0.6f, 0.56f),
                Line = new Color(0.35f, 0.35f, 0.35f),
            },
            ["dark"] = new Palette
            {
                Background = new Color(0.11f, 0.12f, 0.13f),
                Panel = new Color(0.19f, 0.2f, 0.22f),
                HeaderText = Color.white,
                Text = new Color(0.88f, 0.88f, 0.86f),
                TextDim = new Color(0.6f, 0.61f, 0.62f),
                Highlight = Color.white,
                Warning = new Color(1f, 0.45f, 0.4f),
                Done = new Color(0.45f, 0.85f, 0.5f),
                Accent = new Color(0.45f, 0.7f, 1f),
                ButtonFill = new Color(0.24f, 0.25f, 0.27f),
                ButtonPressed = new Color(0.36f, 0.38f, 0.41f),
                Line = new Color(0.45f, 0.46f, 0.48f),
            },
            // After the Hotdog Stand scheme of Windows 3.1.
            ["hotdog"] = new Palette
            {
                Background = new Color(1f, 0.92f, 0f),
                Panel = new Color(0.8f, 0f, 0f),
                HeaderText = Color.white,
                Text = Color.black,
                TextDim = new Color(0.32f, 0.2f, 0f),
                Highlight = Color.black,
                Warning = new Color(0.6f, 0f, 0f),
                Done = new Color(0f, 0.4f, 0f),
                Accent = new Color(0.1f, 0.2f, 0.75f),
                ButtonFill = new Color(1f, 0.35f, 0.25f),
                ButtonPressed = new Color(0.95f, 0.2f, 0.1f),
                Line = Color.black,
            },
            ["colorful"] = new Palette
            {
                Background = new Color(0.97f, 0.97f, 0.94f),
                Panel = new Color(1f, 0.76f, 0.1f),
                HeaderText = new Color(0.05f, 0.15f, 0.5f),
                Text = new Color(0.06f, 0.06f, 0.1f),
                TextDim = new Color(0.3f, 0.3f, 0.4f),
                Highlight = new Color(0.05f, 0.25f, 0.75f),
                Warning = new Color(0.8f, 0.05f, 0.15f),
                Done = new Color(0f, 0.5f, 0.2f),
                Accent = new Color(0.65f, 0.15f, 0.6f),
                ButtonFill = new Color(0.4f, 0.82f, 0.95f),
                ButtonPressed = new Color(0.25f, 0.68f, 0.92f),
                Line = new Color(0.05f, 0.25f, 0.75f),
            },
            ["pastel"] = new Palette
            {
                Background = new Color(0.96f, 0.94f, 0.98f),
                Panel = new Color(0.8f, 0.88f, 0.97f),
                HeaderText = new Color(0.25f, 0.18f, 0.42f),
                Text = new Color(0.18f, 0.18f, 0.26f),
                TextDim = new Color(0.42f, 0.42f, 0.52f),
                Highlight = new Color(0.3f, 0.2f, 0.5f),
                Warning = new Color(0.72f, 0.2f, 0.32f),
                Done = new Color(0.1f, 0.45f, 0.3f),
                Accent = new Color(0.35f, 0.5f, 0.8f),
                ButtonFill = new Color(0.98f, 0.84f, 0.88f),
                ButtonPressed = new Color(0.9f, 0.72f, 0.8f),
                Line = new Color(0.68f, 0.66f, 0.8f),
            },
        };

        /// <summary>The light theme for unknown names.</summary>
        public static Palette PaletteFor(string name) =>
            name != null && Palettes.TryGetValue(name, out var palette) ? palette : Palettes[DefaultPalette];

        /// <summary>Theme of the screen being built or updated.</summary>
        public static Palette Current = PaletteFor(DefaultPalette);

        /// <summary>One of the <see cref="ButtonStyles"/>, for the screen being built or updated.</summary>
        public static string ButtonStyle = ButtonStyles.Simple;

        public static Color Background => Current.Background;

        /// <summary>Same in every theme.</summary>
        public static readonly Color ScreenOff = new Color(0.04f, 0.05f, 0.05f);
        public static Color Panel => Current.Panel;
        public static Color HeaderText => Current.HeaderText;
        public static Color Text => Current.Text;
        public static Color TextDim => Current.TextDim;
        public static Color Highlight => Current.Highlight;

        // Only for things that need attention, like speed drops and misaligned switches.
        public static Color Warning => Current.Warning;

        /// <summary>Finished items, e.g. done shunting plan steps.</summary>
        public static Color Done => Current.Done;

        public static Color Accent => Current.Accent;

        /// <summary>Volume bars and spectrum. Dark theme colors in every theme, so the levels read the same.</summary>
        public static class Meter
        {
            public static readonly Color Frame = new Color(0.19f, 0.2f, 0.22f);
            public static readonly Color Off = new Color(0.15f, 0.16f, 0.18f);
            /// <summary>A volume bar below 60 %.</summary>
            public static readonly Color Normal = new Color(0.88f, 0.88f, 0.86f);
            /// <summary>Spectrum segments below 60 %.</summary>
            public static readonly Color Green = new Color(0.45f, 0.85f, 0.5f);
            /// <summary>From 60 %.</summary>
            public static readonly Color Yellow = new Color(1f, 0.8f, 0.2f);
            /// <summary>From 90 %.</summary>
            public static readonly Color Red = new Color(1f, 0.4f, 0.35f);
        }

        public static Color ButtonFill => Current.ButtonFill;
        public static Color ButtonPressed => Current.ButtonPressed;
        public static Color Line => Current.Line;

        public const int FontSmall = 14;
        public const int FontBody = 17;
        public const int FontTitle = 22;
        public const int FontLarge = 34;

        public const float ScreenWidth = 800f;
        public const float ScreenHeight = 520f;
        public const float HeaderHeight = 40f;
        public const float SoftKeyHeight = 54f;
        public const float Padding = 10f;
        public const float RowHeight = 26f;

        /// <summary>1 px lines step badly when the tablet is seen at an angle.</summary>
        public const float LineWidth = 2f;
    }
}
