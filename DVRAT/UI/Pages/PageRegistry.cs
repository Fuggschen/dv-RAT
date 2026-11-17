using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>All pages by ID. Menus derive from each page's parent and order.</summary>
    public class PageRegistry
    {
        private readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>();
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();

        public void Register(Page page)
        {
            _pages[page.Id] = page;
        }

        public void Clear()
        {
            _pages.Clear();
            _aliases.Clear();
        }

        /// <summary>Lets another ID open the page, e.g. a livery ID for its car type page.</summary>
        public void AddAlias(string alias, string id) => _aliases[alias] = id;

        public string Resolve(string id) => id != null && _aliases.TryGetValue(id, out var target) ? target : id;

        public Page Get(string id) => id != null && _pages.TryGetValue(Resolve(id), out var page) ? page : null;

        public bool Contains(string id) => Get(id) != null;

        public ICollection<string> Ids => _pages.Keys;

        public IReadOnlyList<Page> Children(string parentId) =>
            _pages.Values
                .Where(p => p.ParentId == parentId)
                .OrderBy(p => p.Order)
                .ThenBy(p => p.Title)
                .ToList();
    }
}
