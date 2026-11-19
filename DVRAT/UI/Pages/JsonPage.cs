using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>A page built from a <see cref="PageDefinition"/>, re-evaluated against <see cref="PageBindings"/> on each refresh.</summary>
    public class JsonPage : Page
    {
        private const int MaxPageKeys = PageHost.PageKeySlots;

        private readonly PageDefinition _definition;
        // Replaced in Build by the tablet's copy, which outlives this page object.
        private Dictionary<string, string> _vars;
        private readonly List<Action<Dictionary<string, object>>> _updaters = new List<Action<Dictionary<string, object>>>();
        // Applied before the values: a list that fills the free height needs the final row visibility.
        private readonly List<Action<Dictionary<string, object>>> _visibility = new List<Action<Dictionary<string, object>>>();
        // Paired with the row object so a row hidden by "when" adds no soft keys.
        private readonly List<(PagedList list, GameObject row)> _lists = new List<(PagedList, GameObject)>();
        private readonly List<(IPageWidget widget, GameObject row)> _widgets = new List<(IPageWidget, GameObject)>();
        private readonly HashSet<string> _reportedMissing = new HashSet<string>();
        // Unknown binding paths from templates, logged once per page per load.
        private readonly List<string> _pendingMissing = new List<string>();
        private readonly Dictionary<RowDefinition, IPageWidget> _widgetInstances = new Dictionary<RowDefinition, IPageWidget>();
        private MenuGrid _menuGrid;
        // Null when the page has no menu row or its build failed.
        private MenuGrid _menu;
        private bool _reportedTooManyKeys;

        public JsonPage(PageDefinition definition)
            : base(definition.Id, definition.Title,
                definition.Id == PageIds.Home || definition.Hidden ? null : definition.Parent ?? PageIds.Home,
                definition.Order ?? 100)
        {
            _definition = definition;
            OpenLabel = definition.OpenLabel;
            ParentKey = definition.ParentKey;
            InStartMenu = definition.StartMenu;
            NeedsSignal = definition.Remote;
            _vars = new Dictionary<string, string>(definition.Vars);
            // Matches any binding into the fees in any row or soft key.
            _usesFees = JsonConvert.SerializeObject(definition).Contains("world.fees");
        }

        private readonly bool _usesFees;

        public override bool UsesFees => _usesFees;

        /// <summary>True for a page of only <c>menu</c> rows.</summary>
        public override bool IsMenu => _definition.Rows.Count > 0 && _definition.Rows.All(row => row.Type == "menu");

        public override void Build(PageContext context)
        {
            _builds++;
            if (context.Tablet != null)
                _vars = context.Tablet.Session.Vars(Id, _definition.Vars);
            _updaters.Clear();
            _visibility.Clear();
            _lists.Clear();
            _widgets.Clear();
            _menu = null;

            foreach (var row in _definition.Rows)
            {
                try
                {
                    BuildRow(row, context);
                }
                catch (Exception ex)
                {
                    RATMain.LogError($"Page '{Id}': row \"{row.Type}\" failed: {ex.Message}");
                    UIFactory.Height(UIFactory.Label(context.Content, $"Row \"{row.Type}\" failed: {ex.Message}",
                        Theme.FontSmall, Theme.Warning), Theme.RowHeight);
                }
            }
        }

        // Lets a refresh notice that a widget rebuilt the page.
        private int _builds;

        public override void Refresh(PageContext context)
        {
            var root = Bindings(context);
            foreach (var update in _visibility)
                update(root);
            foreach (var update in _updaters)
                update(root);
            // A widget may rebuild the page, which already refreshed the new widgets.
            int build = _builds;
            foreach (var (widget, _) in _widgets.ToList())
            {
                widget.Refresh(context);
                if (_builds != build) return;
            }

            ReportMissing(_pendingMissing);
            _pendingMissing.Clear();
        }

        // The header asks before the rebuild, while rows of the last visit are destroyed, so check row for null.
        private IBackStep BackStep => _widgets.Where(w => w.row != null && w.row.activeSelf).Select(w => w.widget)
            .OfType<IBackStep>().FirstOrDefault(w => w.CanGoBack);

        public override bool CanGoBack => BackStep != null;

        public override void GoBack(PageContext context) => BackStep?.GoBack(context);

        public override IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            var root = Bindings(context);
            var keys = new List<SoftKey>();
            foreach (var key in _definition.SoftKeys)
            {
                if (!Template.Evaluate(key.When, root, _pendingMissing)) continue;
                bool enabled = Template.Evaluate(key.EnabledWhen, root, _pendingMissing);
                keys.Add(new SoftKey(Template.Format(key.Label, root, _pendingMissing), enabled ? KeyAction(key, root, context) : null,
                    right: key.Right));
            }
            // Paging keys are right keys, added after the page's own so they take the rightmost slot.
            keys.AddRange(_lists.Where(l => l.row.activeSelf).SelectMany(l => l.list.SoftKeys()));
            if (_menu != null)
                keys.AddRange(_menu.SoftKeys(context));
            // Without a menu row, each subpage gets a key. Home has none, the Start menu lists its pages.
            else if (Id != PageIds.Home)
                keys.AddRange(context.Registry.Children(Id).Where(child => child.ParentKey).Select(child =>
                {
                    string childId = child.Id;
                    return new SoftKey(child.Title, () => context.Navigator.Open(childId));
                }));
            keys.AddRange(_widgets.Where(w => w.row.activeSelf).SelectMany(w => w.widget.SoftKeys(context)));

            // See PageHost.SetSoftKeys for how the bar places right keys.
            var left = keys.Where(k => !k.Right).ToList();
            var right = keys.Where(k => k.Right).ToList();
            int rightSlots = SoftKeySlots.Count(right, IsHalf);
            int fitting = SoftKeySlots.Fitting(left, IsHalf, Math.Max(0, MaxPageKeys - rightSlots));
            if (fitting < left.Count && !_reportedTooManyKeys)
            {
                _reportedTooManyKeys = true;
                RATMain.LogWarning($"Page '{Id}' wants {SoftKeySlots.Count(left, IsHalf) + rightSlots} soft key slots, " +
                    $"only {MaxPageKeys} fit: " + string.Join(", ", left.Skip(fitting).Select(k => k.Label)) + " are not shown");
            }
            ReportMissing(_pendingMissing);
            _pendingMissing.Clear();
            return left.Take(fitting).Concat(right);
        }

        private static bool IsHalf(SoftKey key) => key.Half;

        private Action KeyAction(SoftKeyDefinition key, Dictionary<string, object> root, PageContext context)
        {
            string target = null;
            if (!string.IsNullOrEmpty(key.Link))
            {
                target = context.Registry.Resolve(Template.Format(key.Link, root, _pendingMissing));
                if (!context.Registry.Contains(target)) return null;
            }
            var jobIds = string.IsNullOrEmpty(key.PlanJobs) ? null : Template.Values(Template.Format(key.PlanJobs, root, _pendingMissing));
            // Values are templates, e.g. "{settings.brightness + 10}".
            var set = (key.Set ?? new Dictionary<string, string>())
                .ToDictionary(e => e.Key, e => Template.Format(e.Value, root, _pendingMissing));
            if (target == null && jobIds == null && set.Count == 0) return null;

            return () =>
            {
                if (jobIds != null)
                    PlanJobs(jobIds, context);
                bool settingChanged = false;
                foreach (var entry in set)
                {
                    if (entry.Key.StartsWith(SettingsPrefix, StringComparison.Ordinal))
                    {
                        if (context.Tablet == null) continue;
                        string name = entry.Key.Substring(SettingsPrefix.Length);
                        settingChanged |= context.Tablet.Session.Setting(name) != entry.Value;
                        context.Tablet.Session.Settings[name] = entry.Value;
                    }
                    else
                        _vars[entry.Key] = entry.Value;
                }
                // Some settings change what the tablet reads, so read again before the rebuild.
                if (settingChanged)
                    context.Tablet.RefreshDataNow();
                if (target == null)
                {
                    context.Rebuild();
                    return;
                }
                if (key.LinkVars != null && context.Tablet != null)
                    foreach (var entry in key.LinkVars)
                        context.Tablet.Session.Vars(target, null)[entry.Key] = entry.Value;
                context.Navigator.Open(target);
            };
        }

        private const string SettingsPrefix = "settings.";

        private static void PlanJobs(IEnumerable<string> jobIds, PageContext context)
        {
            foreach (string id in jobIds)
                ActiveJobStore.Add(id);
            // The station job list is read only every few seconds, so mark it now.
            foreach (var job in context.World?.StationJobs ?? new List<JobInfo>())
                job.IsActive = ActiveJobStore.IsActive(job.JobId);
        }

        private Dictionary<string, object> Bindings(PageContext context) =>
            PageBindings.Build(context.Train, context.Route, context.Jobs, context.World, _vars, Stations.Name,
                context.Tablet != null ? context.Tablet.Session.AllSettings() : null);

        #region Rows

        private void BuildRow(RowDefinition row, PageContext context)
        {
            Transform parent = context.Content;
            GameObject rowObject;
            switch (row.Type)
            {
                case "text":
                {
                    var style = StyleOf(row.Style);
                    var label = UIFactory.Label(parent, "", style.Size, style.Color, TextAnchor.MiddleLeft, style.FontStyle);
                    UIFactory.Height(label, style.Size >= Theme.FontTitle ? 34f : Theme.RowHeight);
                    Bind(root => label.text = Template.Format(row.Text, root, _pendingMissing));
                    rowObject = label.gameObject;
                    break;
                }
                case "heading":
                {
                    var label = UIFactory.Heading(parent, "");
                    Bind(root => label.text = Template.Format(row.Text, root, _pendingMissing));
                    rowObject = label.gameObject;
                    break;
                }
                case "divider":
                    rowObject = UIFactory.Divider(parent).gameObject;
                    break;
                case "field":
                    rowObject = FieldRow(parent, context, row);
                    break;
                case "pairs":
                {
                    var rowRect = UIFactory.Row(parent);
                    foreach (var pair in row.Items)
                    {
                        var value = UIFactory.Pair(rowRect, pair[0], "", row.LabelWidth ?? 110);
                        string template = pair[1];
                        Bind(root => value.text = Template.Format(template, root, _pendingMissing));
                    }
                    rowObject = rowRect.gameObject;
                    break;
                }
                case "bar":
                    rowObject = BarRow(parent, row);
                    break;
                case "buttons":
                    rowObject = ButtonsRow(parent, context, row);
                    break;
                case "checklist":
                    rowObject = ChecklistRow(parent, context, row);
                    break;
                case "list":
                    rowObject = ListRow(parent, context, row);
                    break;
                case "menu":
                {
                    var column = UIFactory.Column(parent, 4, "Menu");
                    // Kept across rebuilds so the screen survives "Page +".
                    _menu = _menuGrid ?? (_menuGrid = new MenuGrid(Id));
                    _menu.Build(column, context);
                    rowObject = column.gameObject;
                    break;
                }
                case "widget":
                {
                    // Instances persist so widget state survives the rebuilds their buttons trigger.
                    if (!_widgetInstances.TryGetValue(row, out var widget))
                        _widgetInstances[row] = widget = PageWidgets.Create(row.Name, row.Var);
                    var column = UIFactory.Column(parent, 4, "Widget");
                    var element = column.gameObject.AddComponent<LayoutElement>();
                    element.flexibleHeight = 1;
                    widget.Build(column, context, Id, row.Rows);
                    _widgets.Add((widget, column.gameObject));
                    rowObject = column.gameObject;
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown row type \"{row.Type}\"");
            }

            if (!string.IsNullOrEmpty(row.When))
                _visibility.Add(root => rowObject.SetActive(Template.Evaluate(row.When, root, _pendingMissing)));
        }

        /// <summary>
        /// Label and a value that may contain [[links]]. The value is rebuilt only when its text changes.
        /// With <c>lines</c> above 1 the row is that many lines tall and its text wraps, top aligned.
        /// </summary>
        private GameObject FieldRow(Transform parent, PageContext context, RowDefinition row)
        {
            int lines = Math.Max(row.Lines ?? 1, 1);
            bool wrap = lines > 1;
            var anchor = wrap ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            float height = Theme.RowHeight * lines;
            var rowRect = UIFactory.Row(parent, height);
            UIFactory.Width(UIFactory.Label(rowRect, row.Label ?? "", Theme.FontBody, Theme.TextDim, anchor), row.LabelWidth ?? 160);
            var valueRect = UIFactory.Row(rowRect, height, 8, "Value");
            UIFactory.Width(valueRect, 0, 1);
            var style = StyleOf(row.Style);

            string shown = null;
            Bind(root =>
            {
                string text = Template.Format(row.Value, root, _pendingMissing);
                if (text == shown) return;
                shown = text;
                Clear(valueRect);
                foreach (var segment in LinkParser.Parse(text))
                {
                    if (segment.IsLink)
                        LinkButton(valueRect, context, segment.Target, segment.Text);
                    else if (segment.Text.Trim() != "")
                    {
                        var label = UIFactory.Label(valueRect, segment.Text.Trim(), style.Size, style.Color, anchor, style.FontStyle);
                        // Laid out at its full width the text would never wrap.
                        if (wrap) UIFactory.Width(label, 0, 1);
                    }
                }
                if (!wrap) UIFactory.Width(UIFactory.Rect("Spacer", valueRect), 0, 1);
            });
            return rowRect.gameObject;
        }

        /// <summary>Buttons that work like soft keys. The one whose <c>selectedWhen</c> holds is drawn pressed.</summary>
        private GameObject ButtonsRow(Transform parent, PageContext context, RowDefinition row)
        {
            var rowRect = UIFactory.Row(parent, 34, 4, "Buttons");
            if (!string.IsNullOrEmpty(row.Label))
                UIFactory.Width(UIFactory.Label(rowRect, row.Label, Theme.FontSmall, Theme.TextDim), row.LabelWidth ?? 60);
            foreach (var definition in row.Buttons)
            {
                var key = definition;
                if (IsText(key))
                {
                    TextItem(rowRect, key);
                    continue;
                }
                var button = UIFactory.Button(rowRect, "", () =>
                {
                    var action = KeyAction(key, Bindings(context), context);
                    action?.Invoke();
                }, Theme.FontSmall);
                UIFactory.Width(button, 0, 1);
                string style = Theme.ButtonStyle;
                var label = button.GetComponentInChildren<Text>();

                Bind(root =>
                {
                    button.gameObject.SetActive(Template.Evaluate(key.When, root, _pendingMissing));
                    button.interactable = Template.Evaluate(key.EnabledWhen, root, _pendingMissing);
                    bool selected = !string.IsNullOrEmpty(key.SelectedWhen) && Template.Evaluate(key.SelectedWhen, root, _pendingMissing);
                    ButtonStyles.SetSelected(button, style, selected);
                    label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                    label.text = Template.Format(key.Label, root, _pendingMissing);
                });
            }
            return rowRect.gameObject;
        }

        /// <summary>An item with no action is plain text between the buttons.</summary>
        private static bool IsText(SoftKeyDefinition key) =>
            string.IsNullOrEmpty(key.Link) && (key.Set == null || key.Set.Count == 0) && string.IsNullOrEmpty(key.PlanJobs);

        private void TextItem(Transform row, SoftKeyDefinition key)
        {
            var label = UIFactory.Label(row, "", Theme.FontSmall, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Width(label, 0, 1);
            Bind(root =>
            {
                label.gameObject.SetActive(Template.Evaluate(key.When, root, _pendingMissing));
                label.text = Template.Format(key.Label, root, _pendingMissing);
            });
        }

        private GameObject BarRow(Transform parent, RowDefinition row)
        {
            var rowRect = UIFactory.Row(parent);
            UIFactory.Width(UIFactory.Label(rowRect, row.Label ?? "", Theme.FontBody, Theme.TextDim), row.LabelWidth ?? 160);

            var frame = UIFactory.Rect("Bar", rowRect);
            UIFactory.Width(frame, 0, 1);
            frame.gameObject.AddComponent<Image>().color = Theme.Panel;
            var fillRect = UIFactory.Stretch(UIFactory.Rect("Fill", frame), 2, 5, 2, 5);
            var fill = fillRect.gameObject.AddComponent<Image>();

            Text text = null;
            if (!string.IsNullOrEmpty(row.Text))
                text = UIFactory.Width(UIFactory.Label(rowRect, "", Theme.FontBody, Theme.Text, TextAnchor.MiddleRight), 140);

            Bind(root =>
            {
                double value = Number(row.Value, root, 0);
                double min = Number(row.Min, root, 0);
                double max = Number(row.Max, root, 100);
                float fraction = max > min ? Mathf.Clamp01((float)((value - min) / (max - min))) : 0f;
                fillRect.anchorMax = new Vector2(fraction, 1);
                fill.color = !string.IsNullOrEmpty(row.WarnWhen) && Template.Evaluate(row.WarnWhen, root, _pendingMissing)
                    ? Theme.Warning : Theme.Text;
                if (text != null) text.text = Template.Format(row.Text, root, _pendingMissing);
            });
            return rowRect.gameObject;
        }

        private double Number(string expression, Dictionary<string, object> root, double fallback) =>
            !string.IsNullOrWhiteSpace(expression) && Template.TryNumber(expression, root, _pendingMissing, out double number) ? number : fallback;

        /// <summary>Tick boxes for list items. The page variable keeps the unticked values.</summary>
        private GameObject ChecklistRow(Transform parent, PageContext context, RowDefinition row)
        {
            var column = UIFactory.Column(parent, 4, "Checklist");
            int perRow = Math.Max(1, row.PerRow ?? 6);
            string shown = null;

            Bind(root =>
            {
                string path = Template.Format(row.Source, root, _pendingMissing);
                if (!Template.TryResolve(root, path, out object value)) _pendingMissing.Add(path);
                var hidden = Template.Values(_vars.TryGetValue(row.Var, out string text) ? text : "");
                var boxes = ((value as IList)?.Cast<object>() ?? Enumerable.Empty<object>()).Select(item =>
                {
                    var itemRoot = PageBindings.WithItem(root, item);
                    string id = Template.Format(row.Value, itemRoot, _pendingMissing);
                    bool enabled = Template.Evaluate(row.EnabledWhen, itemRoot, _pendingMissing);
                    return (id, label: Template.Format(row.Text, itemRoot, _pendingMissing), enabled, ticked: enabled && !hidden.Contains(id));
                }).ToList();

                string signature = string.Join("|", boxes.Select(b => $"{b.id}:{b.label}:{b.enabled}:{b.ticked}"));
                if (signature == shown) return;
                shown = signature;
                Clear(column);

                if (boxes.Count == 0)
                {
                    UIFactory.Height(UIFactory.Label(column, row.Empty ?? "Nothing to choose.", Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                    return;
                }
                RectTransform line = null;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (i % perRow == 0) line = UIFactory.Row(column, 32, 4, "Boxes");
                    var box = boxes[i];
                    var button = UIFactory.Button(line, (box.ticked ? "[x] " : "[ ] ") + box.label, () =>
                    {
                        var values = Template.Values(_vars.TryGetValue(row.Var, out string current) ? current : "");
                        if (!values.Remove(box.id)) values.Add(box.id);
                        _vars[row.Var] = string.Join(",", values.OrderBy(v => v));
                        context.Rebuild();
                    }, Theme.FontSmall, box.enabled ? Theme.Text : Theme.TextDim);
                    button.interactable = box.enabled;
                    UIFactory.Width(button, 0, 1);
                }
                // Padding keeps the boxes of a short last line the same width.
                for (int i = boxes.Count % perRow; i > 0 && i < perRow; i++)
                    UIFactory.Width(UIFactory.Rect("Gap", line), 0, 1);
            });
            return column.gameObject;
        }

        private GameObject ListRow(Transform parent, PageContext context, RowDefinition row)
        {
            var column = UIFactory.Column(parent, 0, "ListRow");
            if (row.Columns.Any(c => !string.IsNullOrEmpty(c.Header)))
            {
                var header = UIFactory.Row(column, Theme.RowHeight);
                foreach (var definition in row.Columns)
                {
                    var caption = UIFactory.Label(header, definition.Header ?? "", Theme.FontSmall, Theme.TextDim,
                        AlignOf(definition.Align), FontStyle.Bold);
                    if (definition.Width.HasValue)
                        UIFactory.Width(caption, definition.Width.Value);
                    else
                        UIFactory.Width(caption, 0, definition.Flex ?? 1);
                }
                UIFactory.Divider(column);
            }
            // Without "rows" the list fills the free height.
            if (!row.Rows.HasValue)
                column.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
            var list = new PagedList(column, row.Rows ?? 0, row.Empty ?? "Nothing to show.", row.RowHeight ?? Theme.RowHeight,
                context.View(Id), "row" + _lists.Count, row.Source);
            _lists.Add((list, column.gameObject));

            Bind(root =>
            {
                string path = Template.Format(row.Source, root, _pendingMissing);
                IList items = null;
                if (Template.TryResolve(root, path, out object value))
                    items = value as IList;
                else
                    _pendingMissing.Add(path);

                list.SetRows(ListQuery.Apply(items?.Cast<object>() ?? Enumerable.Empty<object>(), root, row, _pendingMissing),
                    (cells, item) =>
                    {
                        var itemRoot = PageBindings.WithItem(root, item);
                        if (!string.IsNullOrEmpty(row.Var))
                            SelectableRow(cells, context, row.Var, Template.Format(row.Value ?? "{item.id}", itemRoot, _pendingMissing));
                        else if (row.Click != null)
                            ClickableRow(cells, context, row.Click, itemRoot);
                        ListCells(cells, row.Columns, itemRoot);
                    });
            });
            return column.gameObject;
        }

        /// <summary>A click toggles the row's value in the page variable. Selected rows are drawn pressed.</summary>
        private void SelectableRow(IRowCells cells, PageContext context, string variable, string value)
        {
            bool selected = Template.Values(_vars.TryGetValue(variable, out string current) ? current : "").Contains(value);
            cells.Selectable(selected, () =>
            {
                var values = Template.Values(_vars.TryGetValue(variable, out string text) ? text : "");
                if (!values.Remove(value)) values.Add(value);
                _vars[variable] = string.Join(",", values.OrderBy(v => v));
                context.Refresh();
            }, does: value);
        }

        /// <summary>Makes the whole row a button that runs <paramref name="key"/> for its item.</summary>
        private void ClickableRow(IRowCells cells, PageContext context, SoftKeyDefinition key, Dictionary<string, object> itemRoot)
        {
            // The action goes into the row key, so a changed action redraws the row.
            var does = new List<string>
            {
                Template.Format(key.Link ?? "", itemRoot, _pendingMissing),
                Template.Format(key.PlanJobs ?? "", itemRoot, _pendingMissing),
            };
            does.AddRange((key.Set ?? new Dictionary<string, string>())
                .Select(entry => entry.Key + "=" + Template.Format(entry.Value, itemRoot, _pendingMissing)));
            cells.Clickable(() => KeyAction(key, itemRoot, context)?.Invoke(), string.Join(";", does));
        }

        private void ListCells(IRowCells cells, List<ColumnDefinition> columns, Dictionary<string, object> root)
        {
            foreach (var column in columns)
            {
                if (!Template.Evaluate(column.When, root, _pendingMissing))
                {
                    // Keeps the width so the other columns stay aligned.
                    if (column.Width.HasValue) cells.Gap(column.Width.Value);
                    continue;
                }

                var style = StyleOf(!string.IsNullOrEmpty(column.WarnWhen) && Template.Evaluate(column.WarnWhen, root, _pendingMissing)
                    ? "warning" : column.Style);
                cells.Label(Template.Format(column.Text, root, _pendingMissing), column.Width ?? 0, column.Flex ?? 1,
                    style.Size, style.Color, AlignOf(column.Align), style.FontStyle);
            }
        }

        private static void LinkButton(RectTransform row, PageContext context, string target, string caption)
        {
            var page = context.Registry.Get(target);
            string text = caption ?? page?.Title ?? target;
            if (page == null)
            {
                UIFactory.Label(row, text, Theme.FontBody, Theme.TextDim);
                return;
            }

            string id = page.Id;
            var button = UIFactory.Button(row, text, () => context.Navigator.Open(id), Theme.FontSmall);
            UIFactory.Width(button, Mathf.Clamp(text.Length * 8 + 20, 60, 240));
        }

        private void Bind(Action<Dictionary<string, object>> update) => _updaters.Add(update);

        private void ReportMissing(IEnumerable<string> missing)
        {
            foreach (string path in missing)
                if (_reportedMissing.Add(path))
                    RATMain.LogWarning($"Page '{Id}': unknown binding '{path}'");
        }

        private static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        #endregion

        #region Styles

        private readonly struct TextStyle
        {
            public TextStyle(Color color, int size, FontStyle fontStyle)
            {
                Color = color;
                Size = size;
                FontStyle = fontStyle;
            }

            public Color Color { get; }
            public int Size { get; }
            public FontStyle FontStyle { get; }
        }

        private static TextStyle StyleOf(string style)
        {
            switch (style)
            {
                case "dim": return new TextStyle(Theme.TextDim, Theme.FontBody, FontStyle.Normal);
                case "highlight": return new TextStyle(Theme.Highlight, Theme.FontBody, FontStyle.Bold);
                case "warning": return new TextStyle(Theme.Warning, Theme.FontBody, FontStyle.Bold);
                case "bold": return new TextStyle(Theme.Text, Theme.FontBody, FontStyle.Bold);
                case "large": return new TextStyle(Theme.Highlight, Theme.FontTitle, FontStyle.Bold);
                default: return new TextStyle(Theme.Text, Theme.FontBody, FontStyle.Normal);
            }
        }

        private static TextAnchor AlignOf(string align)
        {
            switch (align)
            {
                case "right": return TextAnchor.MiddleRight;
                case "center": return TextAnchor.MiddleCenter;
                case "top": return TextAnchor.UpperLeft;
                default: return TextAnchor.MiddleLeft;
            }
        }

        #endregion
    }
}
