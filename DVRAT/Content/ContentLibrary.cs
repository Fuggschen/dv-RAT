using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DVRAT
{
    /// <summary>Rebuilds every page on each tablet boot, so edited <c>pages/*.json</c> show after a power cycle.</summary>
    public static class ContentLibrary
    {
        public const string PagesFolderName = "pages";
        public const string ProblemsPageId = "rat:problems";

        public static void RegisterAll(PageRegistry registry)
        {
            registry.Clear();
            var problems = new List<string>();
            List<PageDefinition> layouts;
            using (Timing.Measure("Reading the page files"))
                layouts = LoadLayouts(PagesFolder(), problems);
            var layoutIds = new HashSet<string>(layouts.Select(l => l.Id));

            var store = new ContentStore();
            GameContent.AddTo(store, problems.Add);
            using (Timing.Measure("Linking the info pages"))
                store.AddBacklinks(layoutIds, problems.Add);

            foreach (var data in store.Pages)
                registry.Register(data.IsMenu
                    ? new MenuPage(data.Id, data.Title, data.ParentId, data.Order)
                    : (Page)new InfoPage(data));
            foreach (var alias in store.Aliases)
                registry.AddAlias(alias.Key, alias.Value);

            // Layouts register last, so one with the same ID as an info page wins.
            foreach (var layout in layouts)
            {
                if (registry.Contains(layout.Id))
                    problems.Add($"pages/{layout.Id}: replaces the info page with the same ID");
                registry.Register(new JsonPage(layout));
            }

            if (!layoutIds.Contains(PageIds.Home))
            {
                problems.Add($"{PagesFolderName}/home.json is missing or invalid; showing a plain menu instead");
                registry.Register(new JsonPage(FallbackHome()));
            }

            foreach (string problem in problems)
                RATMain.LogWarning(problem);
            if (problems.Count > 0)
                registry.Register(new InfoPage(ProblemsPage(problems)));

            RATMain.Log($"Content: {store.Pages.Count()} info pages, {layouts.Count} page layouts, {problems.Count} problems");
        }

        /// <summary>Plain menu plus the error, for when loading failed outright.</summary>
        public static void RegisterFallback(PageRegistry registry, string error)
        {
            registry.Clear();
            registry.Register(new JsonPage(FallbackHome()));
            registry.Register(new InfoPage(ProblemsPage(new List<string> { "Loading pages failed: " + error })));
        }

        private static string PagesFolder()
        {
            string modPath = RATMain.ModEntry?.Path;
            return string.IsNullOrEmpty(modPath) ? null : Path.Combine(modPath, PagesFolderName);
        }

        private static List<PageDefinition> LoadLayouts(string pagesFolder, List<string> problems)
        {
            var layouts = new List<PageDefinition>();
            if (pagesFolder == null || !Directory.Exists(pagesFolder))
            {
                problems.Add($"No {PagesFolderName} folder found in the mod folder");
                return layouts;
            }

            foreach (string file in JsonFiles(pagesFolder))
            {
                string name = $"{PagesFolderName}/{Path.GetFileName(file)}";
                string json = Read(file, name, problems);
                if (json == null) continue;

                var page = PageDefinitions.Parse(json, name, PageWidgets.Names, problems);
                if (page == null) continue;
                if (layouts.Any(l => l.Id == page.Id))
                {
                    problems.Add($"{name}: page ID \"{page.Id}\" is used by another file; this one is skipped");
                    continue;
                }
                layouts.Add(page);
            }
            return layouts;
        }

        private static IEnumerable<string> JsonFiles(string folder) =>
            Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        private static string Read(string file, string name, List<string> problems)
        {
            try
            {
                return File.ReadAllText(file);
            }
            catch (IOException ex)
            {
                problems.Add($"{name}: could not read: {ex.Message}");
                return null;
            }
        }

        private static PageDefinition FallbackHome() => new PageDefinition
        {
            Id = PageIds.Home,
            Title = "R.A.T. Main Menu",
            Vars = new Dictionary<string, string>(),
            SoftKeys = new List<SoftKeyDefinition>(),
            Rows = new List<RowDefinition>
            {
                new RowDefinition { Type = "text", Style = "warning", Text = "home.json could not be loaded. See Content problems." },
                new RowDefinition { Type = "menu" },
            },
        };

        private static InfoPageData ProblemsPage(List<string> problems)
        {
            var page = new InfoPageData(ProblemsPageId, "Content problems", PageIds.Home, 999);
            page.Blocks.Add(InfoBlock.Paragraph("Fix these in the mod's pages folder, then switch the tablet off and on."));
            foreach (string problem in problems)
                page.Blocks.Add(InfoBlock.Paragraph("- " + problem));
            return page;
        }
    }
}
