using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Renders an <see cref="InfoPageData"/> as rows in one paged list.</summary>
    public class InfoPage : Page
    {
        private const int RowsPerScreen = 12;
        private const int CharsPerLine = 84;
        private const int LinksPerRow = 3;

        private readonly InfoPageData _data;
        private PagedList _list;

        public InfoPage(InfoPageData data) : base(data.Id, data.Title, data.ParentId, data.Order)
        {
            _data = data;
        }

        public override void Build(PageContext context)
        {
            _list = new PagedList(context.Content, RowsPerScreen, "No information yet.", Theme.RowHeight, context.View(Id), "info");
            _list.SetGroups(Groups(context));
        }

        /// <summary>Groups rows so a screen break never splits a paragraph or separates a heading from what follows.</summary>
        private List<IReadOnlyList<Action<RectTransform>>> Groups(PageContext context)
        {
            Func<string, string> titleOf = id => context.Registry.Get(id)?.Title ?? id;
            var textLinks = new List<string>();
            var groups = new List<List<Action<RectTransform>>>();
            var headings = new List<Action<RectTransform>>();

            void Add(List<Action<RectTransform>> rows, bool keepWithPrevious)
            {
                if (rows.Count == 0) return;
                if (keepWithPrevious && groups.Count > 0 && headings.Count == 0)
                {
                    groups[groups.Count - 1].AddRange(rows);
                    return;
                }
                rows.InsertRange(0, headings);
                headings.Clear();
                groups.Add(rows);
            }

            foreach (var block in _data.Blocks)
            {
                switch (block.Kind)
                {
                    case InfoBlockKind.Field:
                        Add(new List<Action<RectTransform>> { row => FieldRow(row, context, block) }, block.KeepWithPrevious);
                        break;

                    case InfoBlockKind.Heading:
                        headings.Add(row => UIFactory.Width(UIFactory.Label(row, block.Text, Theme.FontBody, Theme.Highlight,
                            TextAnchor.LowerLeft, FontStyle.Bold), 0, 1));
                        break;

                    case InfoBlockKind.Text:
                        var lines = LinkParser.Wrap(LinkParser.ToPlainText(block.Text, titleOf), CharsPerLine)
                            .Select(line => (Action<RectTransform>)(row => UIFactory.Width(UIFactory.Label(row, line), 0, 1)))
                            .ToList();
                        Add(lines, block.KeepWithPrevious);
                        textLinks.AddRange(LinkParser.Targets(block.Text));
                        break;

                    case InfoBlockKind.Links:
                        Add(LinkRows(context, block.Label, block.Targets).ToList(), block.KeepWithPrevious);
                        break;
                }
            }

            // Links in paragraphs are not clickable, so they get buttons at the end.
            Add(LinkRows(context, "Mentioned", textLinks.Distinct().ToList()).ToList(), false);
            if (headings.Count > 0)
                groups.Add(new List<Action<RectTransform>>(headings));

            return groups.Cast<IReadOnlyList<Action<RectTransform>>>().ToList();
        }

        private static void FieldRow(RectTransform row, PageContext context, InfoBlock field)
        {
            UIFactory.Width(UIFactory.Label(row, field.Label, Theme.FontBody, Theme.TextDim), 160);
            foreach (var segment in LinkParser.Parse(field.Text))
            {
                if (segment.IsLink)
                    LinkButton(row, context, segment.Target, segment.Text);
                else if (segment.Text.Trim(' ', ',') != "")
                    UIFactory.Label(row, segment.Text.Trim(' ', ','));
            }
            UIFactory.Width(UIFactory.Rect("Spacer", row), 0, 1);
        }

        private static IEnumerable<Action<RectTransform>> LinkRows(PageContext context, string caption, List<string> targets)
        {
            if (targets.Count == 0) yield break;
            if (!string.IsNullOrEmpty(caption))
                yield return row => UIFactory.Width(UIFactory.Label(row, caption, Theme.FontBody, Theme.TextDim), 0, 1);

            for (int i = 0; i < targets.Count; i += LinksPerRow)
            {
                var chunk = targets.Skip(i).Take(LinksPerRow).ToList();
                yield return row =>
                {
                    foreach (string target in chunk)
                        UIFactory.Width(LinkButton(row, context, target, null), 0, 1);
                    for (int empty = chunk.Count; empty < LinksPerRow; empty++)
                        UIFactory.Width(UIFactory.Rect("Spacer", row), 0, 1);
                };
            }
        }

        /// <summary>Opens the target page, or shows dim text when the page does not exist.</summary>
        private static Component LinkButton(RectTransform row, PageContext context, string target, string caption)
        {
            var page = context.Registry.Get(target);
            string text = caption ?? page?.Title ?? target;
            if (page == null)
                return UIFactory.Label(row, text, Theme.FontBody, Theme.TextDim);

            string id = page.Id;
            var button = UIFactory.Button(row, text, () => context.Navigator.Open(id), Theme.FontSmall);
            return UIFactory.Width(button, Mathf.Clamp(text.Length * 8 + 20, 60, 240));
        }

        public override IEnumerable<SoftKey> SoftKeys(PageContext context) => _list?.SoftKeys() ?? Enumerable.Empty<SoftKey>();
    }
}
