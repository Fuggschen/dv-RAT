using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>A page that only shows the button grid of its child pages.</summary>
    public class MenuPage : Page
    {
        private readonly MenuGrid _grid;

        public MenuPage(string id, string title, string parentId = PageIds.Home, int order = 100)
            : base(id, title, parentId, order)
        {
            _grid = new MenuGrid(id);
        }

        public override bool IsMenu => true;

        public override void Build(PageContext context) => _grid.Build(context.Content, context);

        public override IEnumerable<SoftKey> SoftKeys(PageContext context) => _grid.SoftKeys(context);
    }

    /// <summary>Paged buttons for every page whose parent is <c>menuId</c>.</summary>
    public class MenuGrid
    {
        private const int Columns = 2;
        private const int RowsPerScreen = 6;
        private const int EntriesPerScreen = Columns * RowsPerScreen;
        private const float ButtonHeight = 44f;

        private readonly string _menuId;
        // Kept in the page's view state so every tablet copy shows the same screen.
        private const string ScreenKey = "screen";

        private int StoredScreen(PageContext context)
        {
            var view = context.View(_menuId);
            return view != null && view.TryGetValue(ScreenKey, out string saved) && int.TryParse(saved, out int screen) ? screen : 0;
        }

        private void Store(PageContext context, int screen)
        {
            var view = context.View(_menuId);
            if (view != null) view[ScreenKey] = screen.ToString();
        }

        public MenuGrid(string menuId)
        {
            _menuId = menuId;
        }

        public void Build(Transform parent, PageContext context)
        {
            var entries = context.Registry.Children(_menuId);
            if (entries.Count == 0)
            {
                UIFactory.Height(UIFactory.Label(parent, "No pages yet.", Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                return;
            }

            // Removed pages can leave fewer screens than the stored one.
            int screenShown = Mathf.Clamp(StoredScreen(context), 0, ScreenCount(entries.Count) - 1);

            var gridRect = UIFactory.Rect("Grid", parent);
            var grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2((Theme.ScreenWidth - 2 * Theme.Padding - 10) / Columns, ButtonHeight);
            grid.spacing = new Vector2(10, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;

            int first = screenShown * EntriesPerScreen;
            int shown = Mathf.Min(EntriesPerScreen, entries.Count - first);
            UIFactory.Height(gridRect, (shown + Columns - 1) / Columns * (ButtonHeight + 8));

            for (int i = first; i < first + shown; i++)
            {
                string target = entries[i].Id;
                UIFactory.Button(gridRect, entries[i].Title, () => context.Navigator.Open(target));
            }

            if (ScreenCount(entries.Count) > 1)
                UIFactory.Height(UIFactory.Label(parent, $"Page {screenShown + 1}/{ScreenCount(entries.Count)}",
                    Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), Theme.RowHeight);
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            int screens = ScreenCount(context.Registry.Children(_menuId).Count);
            if (screens < 2) yield break;

            int screen = Mathf.Clamp(StoredScreen(context), 0, screens - 1);
            yield return new SoftKey("Page\n-", () => { Store(context, (screen + screens - 1) % screens); context.Rebuild(); }, half: true, right: true);
            yield return new SoftKey("Page\n+", () => { Store(context, (screen + 1) % screens); context.Rebuild(); }, half: true, right: true);
        }

        private static int ScreenCount(int entries) => Mathf.Max(1, (entries + EntriesPerScreen - 1) / EntriesPerScreen);
    }
}
