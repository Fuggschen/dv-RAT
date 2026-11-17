using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The list that rises from the Start key. Entries with subpages open a further list beside it.</summary>
    public class StartMenu
    {
        private const float PanelWidth = 200f;
        private const float EntryHeight = 36f;
        private const float Gap = 2f;
        private const float Inset = 4f;

        private class Entry
        {
            public string Title;
            public Action Open;
            public List<Entry> Children;
        }

        private readonly PageRegistry _registry;
        private readonly Navigator _navigator;
        private GameObject _root;

        public StartMenu(PageRegistry registry, Navigator navigator)
        {
            _registry = registry;
            _navigator = navigator;
        }

        // Unity null check: the object is destroyed when its host is replaced.
        public bool IsOpen => _root != null;

        public void Toggle(RectTransform host)
        {
            if (IsOpen) Close();
            else Open(host);
        }

        public void Close()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        private void Open(RectTransform host)
        {
            var root = UIFactory.Stretch(UIFactory.Rect("StartMenu", host));
            _root = root.gameObject;

            // A tap outside the lists closes the menu, even on the Start key.
            var blocker = UIFactory.Stretch(UIFactory.Rect("Blocker", root));
            var hit = blocker.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            blocker.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            // Panel clamps top 0 up to the soft key bar.
            Panel(root, Entries(), 0, 0, 0);
        }

        private List<Entry> Entries() => ListedChildren(PageIds.Home).Select(PageEntry).ToList();

        private List<Page> ListedChildren(string id) => _registry.Children(id).Where(page => page.InStartMenu).ToList();

        private Entry PageEntry(Page page)
        {
            string id = page.Id;
            var entry = new Entry { Title = page.Title, Open = () => _navigator.Open(id) };
            // A menu inside a menu would list too many entries, e.g. every station, so it opens as a page.
            bool nestedMenu = page.IsMenu && _registry.Get(page.ParentId)?.IsMenu == true;
            var children = ListedChildren(id);
            if (children.Count > 0 && !nestedMenu)
            {
                entry.Children = new List<Entry>();
                // A menu page gets no entry of its own.
                if (!page.IsMenu)
                    entry.Children.Add(new Entry { Title = page.OpenLabel ?? page.Title, Open = entry.Open });
                entry.Children.AddRange(children.Select(PageEntry));
            }
            return entry;
        }

        private static float Height(int entries) => 2 * Inset + entries * EntryHeight + (entries - 1) * Gap;

        /// <summary>Top edge is <paramref name="top"/> px above the screen bottom, clamped to the area above the soft key bar.</summary>
        /// <param name="level">0 for the first list, which also holds Desktop.</param>
        private void Panel(RectTransform root, List<Entry> entries, float left, float top, int level)
        {
            bool withDesktop = level == 0;
            // Entries that do not fit are cut. The page itself lists all its subpages.
            float maxHeight = Theme.ScreenHeight - Theme.SoftKeyHeight;
            int fitting = Math.Max(1, (int)((maxHeight - 2 * Inset + Gap) / (EntryHeight + Gap)) - (withDesktop ? 1 : 0));
            entries = entries.Take(fitting).ToList();
            int rows = entries.Count + (withDesktop ? 1 : 0);
            float height = Height(rows) + (withDesktop ? Theme.LineWidth + Gap : 0);
            top = Mathf.Clamp(top, Theme.SoftKeyHeight + height, Theme.ScreenHeight);

            // Deep lists overlap the previous one at the screen's right edge.
            left = Mathf.Min(left, Theme.ScreenWidth - PanelWidth);
            var panel = UIFactory.Rect(PanelName + level, root);
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            panel.anchoredPosition = new Vector2(left, top - height);
            panel.sizeDelta = new Vector2(PanelWidth, height);
            // The frame sets the list apart from the page, which has the same colours.
            UIFactory.Fill(panel, Theme.Line, "Frame");
            var fill = UIFactory.Fill(panel, Theme.Panel);
            UIFactory.Stretch(fill.rectTransform, Theme.LineWidth, Theme.LineWidth, Theme.LineWidth, Theme.LineWidth);
            // Keeps taps on the frame or between entries from closing the menu.
            fill.raycastTarget = true;

            var column = UIFactory.Column(panel, Gap, "Entries");
            UIFactory.Stretch(column, Inset, Inset, Inset, Inset);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                float entryTop = top - Inset - i * (EntryHeight + Gap);
                Button(column, entry, () =>
                {
                    if (entry.Children == null)
                    {
                        Close();
                        entry.Open();
                        return;
                    }
                    ClosePanels(root, level + 1);
                    Panel(root, entry.Children, left + PanelWidth, entryTop + Inset, level + 1);
                });
            }

            if (withDesktop)
            {
                UIFactory.Divider(column);
                Button(column, new Entry { Title = "Desktop" }, () =>
                {
                    Close();
                    _navigator.Home();
                });
            }
        }

        private const string PanelName = "Panel";

        /// <summary>Removes the lists from <paramref name="level"/> on.</summary>
        private static void ClosePanels(RectTransform root, int level)
        {
            for (var panel = root.Find(PanelName + level); panel != null; panel = root.Find(PanelName + ++level))
            {
                // Destroy runs at frame end, but the new list takes this name now.
                panel.name = "Closed";
                panel.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(panel.gameObject);
            }
        }

        private static void Button(Transform column, Entry entry, Action onClick)
        {
            var button = UIFactory.Height(UIFactory.Button(column, entry.Title, onClick), EntryHeight);
            var caption = button.GetComponentInChildren<Text>();
            caption.alignment = TextAnchor.MiddleLeft;
            caption.rectTransform.offsetMin = new Vector2(10, 0);
            if (entry.Children == null) return;
            var arrow = UIFactory.Label(button.transform, ">", Theme.FontBody, null, TextAnchor.MiddleRight);
            UIFactory.Stretch(arrow.rectTransform, 0, 0, 10, 0);
        }
    }
}
