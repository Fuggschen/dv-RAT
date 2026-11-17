using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Content of one info page, independent of Unity. Rendered by <see cref="InfoPage"/>.</summary>
    public class InfoPageData
    {
        public InfoPageData(string id, string title, string parentId, int order = 100)
        {
            Id = id;
            Title = title;
            ParentId = parentId;
            Order = order;
        }

        public string Id { get; }
        public string Title { get; set; }
        public string ParentId { get; set; }
        public int Order { get; set; }

        /// <summary>A menu lists its children instead of showing blocks.</summary>
        public bool IsMenu { get; set; }

        public List<InfoBlock> Blocks { get; } = new List<InfoBlock>();

        public IEnumerable<string> LinkTargets() => Blocks.SelectMany(b => b.LinkTargets()).Distinct();
    }

    public enum InfoBlockKind
    {
        /// <summary>Label and a one-line value that may contain [[links]].</summary>
        Field,
        Heading,
        /// <summary>Wrapped paragraph. Its [[links]] become buttons below.</summary>
        Text,
        Links,
    }

    public class InfoBlock
    {
        private InfoBlock(InfoBlockKind kind, string label, string text, List<string> targets)
        {
            Kind = kind;
            Label = label;
            Text = text;
            Targets = targets ?? new List<string>();
        }

        public InfoBlockKind Kind { get; }
        public string Label { get; }
        public string Text { get; }
        public List<string> Targets { get; }

        /// <summary>Stays on the same screen as the previous block.</summary>
        public bool KeepWithPrevious { get; private set; }

        public InfoBlock WithPrevious()
        {
            KeepWithPrevious = true;
            return this;
        }

        public static InfoBlock Field(string label, string value) => new InfoBlock(InfoBlockKind.Field, label, value, null);
        public static InfoBlock Heading(string text) => new InfoBlock(InfoBlockKind.Heading, null, text, null);
        public static InfoBlock Paragraph(string text) => new InfoBlock(InfoBlockKind.Text, null, text, null);

        public static InfoBlock Links(string caption, IEnumerable<string> targets) =>
            new InfoBlock(InfoBlockKind.Links, caption, null, targets.Distinct().ToList());

        public IEnumerable<string> LinkTargets() =>
            Kind == InfoBlockKind.Links ? Targets : LinkParser.Targets(Text);
    }
}
