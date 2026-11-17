using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>
    /// List area split into screens, flipped with the soft keys from <see cref="SoftKeys"/>. Without a fixed row count it fills the free height.
    /// </summary>
    public class PagedList
    {
        private const float IndicatorHeight = Theme.FontSmall + 4;

        private const float RowSpacing = 2f;

        private readonly RectTransform _root;
        private readonly bool _fill;
        private int _rowsPerScreen;
        private readonly float _rowHeight;
        private readonly string _emptyText;
        private readonly List<Action<RectTransform>> _rows = new List<Action<RectTransform>>();
        private List<int> _screenStarts = new List<int> { 0 };
        private int _screen;
        private readonly int _builtFrame;
        // See TabletSession.ViewVars. Null forgets the screen on rebuild.
        private readonly IDictionary<string, string> _view;
        private readonly string _viewKey;
        private readonly string _content;

        /// <param name="rowsPerScreen">0 or less fills the free height.</param>
        /// <param name="view">Keeps the screen so every copy of the tablet shows the same.</param>
        /// <param name="content">Other content starts on the first screen.</param>
        public PagedList(Transform parent, int rowsPerScreen, string emptyText, float rowHeight = Theme.RowHeight,
            IDictionary<string, string> view = null, string key = "list", string content = "")
        {
            _builtFrame = Time.frameCount;
            if (view != null)
            {
                _view = view;
                _viewKey = TabletSession.ListKeyPrefix + key;
                _content = content ?? "";
                _screen = SavedScreen();
            }
            _fill = rowsPerScreen <= 0;
            _rowsPerScreen = Math.Max(1, rowsPerScreen);
            _rowHeight = rowHeight;
            _emptyText = emptyText;
            _root = UIFactory.Column(parent, RowSpacing, "List");
            if (_fill)
            {
                var element = _root.gameObject.AddComponent<LayoutElement>();
                element.minHeight = rowHeight + IndicatorHeight;
                element.flexibleHeight = 1;
            }
            else
            {
                UIFactory.Height(_root, rowsPerScreen * (rowHeight + RowSpacing) + IndicatorHeight);
            }
        }

        public int ScreenCount => _screenStarts.Count;

        /// <summary>Redrawn only when a row describes itself differently, see <see cref="IRowCells"/>.</summary>
        public void SetRows<T>(IEnumerable<T> items, Action<IRowCells, T> describe)
        {
            var groups = new List<IReadOnlyList<Action<IRowCells>>>();
            if (items != null)
                foreach (var item in items)
                    groups.Add(new Action<IRowCells>[] { cells => describe(cells, item) });
            SetGroups(groups);
        }

        /// <summary>A group lands on one screen if it fits. Redrawn only when a row describes itself differently.</summary>
        public void SetGroups(IEnumerable<IReadOnlyList<Action<IRowCells>>> groups)
        {
            var builders = new List<IReadOnlyList<Action<RectTransform>>>();
            var keys = new List<string>();
            foreach (var group in groups)
            {
                builders.Add(group.Select(describe => (Action<RectTransform>)(row => describe(new DrawnCells(row)))).ToList());
                foreach (var describe in group)
                {
                    var key = new KeyCells();
                    describe(key);
                    keys.Add(key.Key);
                }
            }
            SetGroups(builders, keys);
        }

        /// <summary>Redrawn on every call.</summary>
        public void SetGroups(IEnumerable<IReadOnlyList<Action<RectTransform>>> groups) => SetGroups(groups, null);

        // What the screen was drawn from. Redrawing the same rows every refresh took about 10 ms.
        private List<string> _rowKeys;
        private List<string> _drawnKeys;
        private int _drawnScreen = -1;
        private int _drawnRowsPerScreen;

        private void SetGroups(IEnumerable<IReadOnlyList<Action<RectTransform>>> groups, List<string> keys)
        {
            _rowKeys = keys;
            _rows.Clear();
            var sizes = new List<int>();
            foreach (var group in groups)
            {
                if (group.Count == 0) continue;
                _rows.AddRange(group);
                sizes.Add(group.Count);
            }
            if (_fill)
                _rowsPerScreen = RowsThatFit(_rows.Count);
            _screenStarts = Pagination.ScreenStarts(sizes, _rowsPerScreen);
            _screen = Mathf.Clamp(_screen, 0, ScreenCount - 1);
            if (keys != null && _drawnKeys != null && _screen == _drawnScreen && _rowsPerScreen == _drawnRowsPerScreen
                && keys.SequenceEqual(_drawnKeys))
                return;
            Redraw();
        }

        /// <summary>The screen indicator only takes room with more than one screen.</summary>
        private int RowsThatFit(int rowCount)
        {
            // The layout has not run on a just-built page. Forcing it on every refresh cost a few ms.
            if (Time.frameCount == _builtFrame)
            {
                var outer = _root;
                while (outer.parent is RectTransform parent && parent.GetComponent<LayoutGroup>() != null)
                    outer = parent;
                LayoutRebuilder.ForceRebuildLayoutImmediate(outer);
            }

            float height = _root.rect.height + RowSpacing;
            int all = Mathf.FloorToInt(height / (_rowHeight + RowSpacing));
            if (rowCount <= all) return Math.Max(1, all);
            return Math.Max(1, Mathf.FloorToInt((height - IndicatorHeight) / (_rowHeight + RowSpacing)));
        }

        public void SetLines(IEnumerable<string> lines, Color? color = null)
        {
            SetRows(lines, (cells, line) => cells.Label(line, color: color));
        }

        /// <summary>"Page -" and "Page +" keys, only with more than one screen. They share the rightmost slot.</summary>
        public IEnumerable<SoftKey> SoftKeys()
        {
            if (ScreenCount < 2) yield break;
            yield return new SoftKey("Page\n-", () => Step(-1), half: true, right: true);
            yield return new SoftKey("Page\n+", () => Step(+1), half: true, right: true);
        }

        private void Step(int delta)
        {
            _screen = (_screen + delta + ScreenCount) % ScreenCount;
            if (_view != null)
                _view[_viewKey] = _content + "|" + _screen;
            Redraw();
        }

        // Stored as "content|screen".
        private int SavedScreen()
        {
            if (!_view.TryGetValue(_viewKey, out string saved)) return 0;
            int split = saved.LastIndexOf('|');
            return split >= 0 && saved.Substring(0, split) == _content && int.TryParse(saved.Substring(split + 1), out int screen)
                ? Math.Max(0, screen)
                : 0;
        }

        /// <summary>Builds while the list is inactive. A UI component dirties the canvas layout on every property set while active, which made a page build take about three frames.</summary>
        private void Redraw()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                var child = _root.GetChild(i).gameObject;
                // Destroy runs at frame end, so keep the old row out of the layout until then.
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }

            var list = _root.gameObject;
            bool active = list.activeSelf;
            list.SetActive(false);
            try
            {
                Fill();
            }
            finally
            {
                list.SetActive(active);
            }
            _drawnKeys = _rowKeys;
            _drawnScreen = _screen;
            _drawnRowsPerScreen = _rowsPerScreen;
        }

        private void Fill()
        {
            if (_rows.Count == 0)
            {
                UIFactory.Height(UIFactory.Label(_root, _emptyText, Theme.FontBody, Theme.TextDim), _rowHeight);
                return;
            }

            int first = _screenStarts[_screen];
            int end = _screen + 1 < ScreenCount ? _screenStarts[_screen + 1] : _rows.Count;
            for (int i = first; i < end; i++)
                _rows[i](UIFactory.Row(_root, _rowHeight));

            if (ScreenCount > 1)
                UIFactory.Height(UIFactory.Label(_root, $"{_screen + 1}/{ScreenCount}", Theme.FontSmall, Theme.TextDim,
                    TextAnchor.MiddleRight), IndicatorHeight);
        }
    }
}
