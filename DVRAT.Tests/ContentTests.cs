using System.Collections.Generic;
using System.Linq;
using DVRAT;
using Xunit;

public class LinkParserTests
{
    [Fact]
    public void SplitsTextAndLinks()
    {
        var segments = LinkParser.Parse("Coal from [[station:CM]] to [[station:SM|the mill]].");

        Assert.Equal(5, segments.Count);
        Assert.Equal("station:CM", segments[1].Target);
        Assert.Null(segments[1].Text);
        Assert.Equal("station:SM", segments[3].Target);
        Assert.Equal("the mill", segments[3].Text);
        Assert.Equal(".", segments[4].Text);
    }

    [Fact]
    public void PlainTextUsesCaptionOrTitle()
    {
        string text = LinkParser.ToPlainText("See [[station:HB]] and [[cargo:Coal|coal]].", id => id == "station:HB" ? "Harbor" : "?");

        Assert.Equal("See Harbor and coal.", text);
    }

    [Fact]
    public void TextWithoutLinksIsOneSegment()
    {
        var segments = LinkParser.Parse("no links here");

        Assert.Single(segments);
        Assert.False(segments[0].IsLink);
    }

    [Fact]
    public void WrapKeepsLinesWithinLimitAndBreaksAtWords()
    {
        var lines = LinkParser.Wrap("the quick brown fox jumps over the lazy dog", 10);

        Assert.All(lines, line => Assert.True(line.Length <= 10));
        Assert.Equal("the quick brown fox jumps over the lazy dog", string.Join(" ", lines));
    }

    [Fact]
    public void WrapCutsWordsLongerThanALine()
    {
        var lines = LinkParser.Wrap("abcdefghijkl", 5);

        Assert.Equal(new[] { "abcde", "fghij", "kl" }, lines);
    }

    [Fact]
    public void WrapKeepsExplicitLineBreaks()
    {
        Assert.Equal(new[] { "one", "two" }, LinkParser.Wrap("one\ntwo", 20));
    }
}

public class ContentStoreTests
{
    private static InfoPageData Page(string id, params InfoBlock[] blocks)
    {
        var page = new InfoPageData(id, id.ToUpper(), "info");
        page.Blocks.AddRange(blocks);
        return page;
    }

    [Fact]
    public void AddingSameIdAppendsBlocks()
    {
        var store = new ContentStore();
        store.Add(Page("station:HB", InfoBlock.Field("Type", "Harbor")));
        store.Add(Page("station:HB", InfoBlock.Paragraph("Ships containers.")));

        Assert.Equal(2, store.Get("station:HB").Blocks.Count);
    }

    [Fact]
    public void BacklinksListPagesThatLinkHere()
    {
        var store = new ContentStore();
        store.Add(Page("station:HB"));
        store.Add(Page("cargo:Containers", InfoBlock.Links("Shipped from", new[] { "station:HB" })));

        store.AddBacklinks(new List<string>(), null);

        var backlinks = store.Get("station:HB").Blocks.Single(b => b.Label == "Linked from");
        Assert.Equal(new[] { "cargo:Containers" }, backlinks.Targets);
    }

    [Fact]
    public void BacklinksSkipPagesAlreadyLinked()
    {
        var store = new ContentStore();
        store.Add(Page("a", InfoBlock.Paragraph("see [[b]]")));
        store.Add(Page("b", InfoBlock.Paragraph("see [[a]]")));

        store.AddBacklinks(new List<string>(), null);

        Assert.DoesNotContain(store.Get("a").Blocks, b => b.Label == "Linked from");
    }

    [Fact]
    public void AliasesResolveForLookupAndBacklinks()
    {
        var store = new ContentStore();
        store.Add(Page("car:LocoShunterType"));
        store.AddAlias("car:LocoShunter", "car:LocoShunterType");
        store.Add(Page("cargo:Coal", InfoBlock.Links(null, new[] { "car:LocoShunter" })));

        store.AddBacklinks(new List<string>(), null);

        Assert.Same(store.Get("car:LocoShunterType"), store.Get("car:LocoShunter"));
        Assert.Contains(store.Get("car:LocoShunterType").Blocks, b => b.Label == "Linked from");
    }

    [Fact]
    public void UnknownLinksAreReportedUnlessAnotherPageHasTheId()
    {
        var store = new ContentStore();
        store.Add(Page("a", InfoBlock.Paragraph("[[missing]] and [[jobs]]")));
        var warnings = new List<string>();

        store.AddBacklinks(new List<string> { "jobs" }, warnings.Add);

        Assert.Single(warnings);
        Assert.Contains("missing", warnings[0]);
    }
}
