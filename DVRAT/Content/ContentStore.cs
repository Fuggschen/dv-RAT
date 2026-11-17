using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Collects generated info pages, resolves aliases and adds "Linked from" backlinks.</summary>
    public class ContentStore
    {
        private readonly Dictionary<string, InfoPageData> _pages = new Dictionary<string, InfoPageData>();
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();

        public IEnumerable<InfoPageData> Pages => _pages.Values;

        public InfoPageData Get(string id) => id != null && _pages.TryGetValue(Resolve(id), out var page) ? page : null;

        /// <summary>A page with an existing ID gets the new blocks appended.</summary>
        public void Add(InfoPageData page)
        {
            if (_pages.TryGetValue(page.Id, out var existing))
                existing.Blocks.AddRange(page.Blocks);
            else
                _pages[page.Id] = page;
        }

        /// <summary>Points an alias at a page, e.g. a livery ID at its car type.</summary>
        public void AddAlias(string alias, string id)
        {
            if (alias != id) _aliases[alias] = id;
        }

        public string Resolve(string id) => id != null && _aliases.TryGetValue(id, out var target) ? target : id;

        public IReadOnlyDictionary<string, string> Aliases => _aliases;

        /// <summary>Adds "Linked from" blocks and warns about links to pages missing here and in <paramref name="otherPageIds"/>. Call once, after merging.</summary>
        public void AddBacklinks(ICollection<string> otherPageIds, Action<string> warn)
        {
            var backlinks = new Dictionary<string, List<string>>();
            foreach (var page in _pages.Values.OrderBy(p => p.Title))
            {
                foreach (string rawTarget in page.LinkTargets())
                {
                    string target = Resolve(rawTarget);
                    if (_pages.ContainsKey(target))
                    {
                        if (target == page.Id) continue;
                        if (!backlinks.TryGetValue(target, out var sources))
                            backlinks[target] = sources = new List<string>();
                        sources.Add(page.Id);
                    }
                    else if (!otherPageIds.Contains(target))
                    {
                        warn?.Invoke($"Page '{page.Id}' links to unknown page '{rawTarget}'");
                    }
                }
            }

            foreach (var entry in backlinks)
            {
                var page = _pages[entry.Key];
                // Skip links the page already shows.
                var shown = new HashSet<string>(page.LinkTargets().Select(Resolve));
                var sources = entry.Value.Where(id => !shown.Contains(id)).ToList();
                if (sources.Count > 0 && !page.IsMenu)
                    page.Blocks.Add(InfoBlock.Links("Linked from", sources));
            }
        }
    }
}
