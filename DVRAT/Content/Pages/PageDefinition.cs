using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DVRAT
{
    /// <summary>A page as written in <c>content/pages/*.json</c>. Row types and binding paths: <c>content/pages/README.md</c>.</summary>
    public class PageDefinition
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("title")] public string Title { get; set; }
        [JsonProperty("parent")] public string Parent { get; set; }
        [JsonProperty("order")] public int? Order { get; set; }

        /// <summary>Reached only by links, e.g. the header clock.</summary>
        [JsonProperty("hidden")] public bool Hidden { get; set; }

        /// <summary>The page's own Start menu entry above its subpages. Defaults to the title.</summary>
        [JsonProperty("openLabel")] public string OpenLabel { get; set; }

        /// <summary>False hides the parent's soft key for this page. The Start menu still lists it.</summary>
        [JsonProperty("parentKey")] public bool ParentKey { get; set; } = true;

        /// <summary>False keeps the page out of the Start menu.</summary>
        [JsonProperty("startMenu")] public bool StartMenu { get; set; } = true;

        /// <summary>Each visit starts with a loading bar whose length follows the radio signal.</summary>
        [JsonProperty("remote")] public bool Remote { get; set; }

        /// <summary>Start values of the page variables, read as <c>page.name</c>.</summary>
        [JsonProperty("vars")] public Dictionary<string, string> Vars { get; set; }

        [JsonProperty("rows")] public List<RowDefinition> Rows { get; set; }
        [JsonProperty("softKeys")] public List<SoftKeyDefinition> SoftKeys { get; set; }
    }

    public class RowDefinition
    {
        [JsonProperty("type")] public string Type { get; set; }

        /// <summary>The row shows only while this condition holds.</summary>
        [JsonProperty("when")] public string When { get; set; }

        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
        [JsonProperty("style")] public string Style { get; set; }
        [JsonProperty("labelWidth")] public float? LabelWidth { get; set; }

        /// <summary>For "pairs": <c>[["Speed", "{train.speedKmh:F0} km/h"]]</c>.</summary>
        [JsonProperty("items")] public List<List<string>> Items { get; set; }

        /// <summary>For "list": binding path, may contain placeholders.</summary>
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("rows")] public int? Rows { get; set; }
        [JsonProperty("rowHeight")] public float? RowHeight { get; set; }
        [JsonProperty("empty")] public string Empty { get; set; }
        [JsonProperty("columns")] public List<ColumnDefinition> Columns { get; set; }

        /// <summary>For "list": condition per item, evaluated with <c>item</c>.</summary>
        [JsonProperty("filter")] public string Filter { get; set; }

        /// <summary>For "list": path or arithmetic to sort by, e.g. <c>item.pay</c>. May be a placeholder like <c>{page.sort}</c>.</summary>
        [JsonProperty("sort")] public string Sort { get; set; }

        /// <summary>For "list": <c>true</c>, <c>false</c> or a condition such as <c>page.desc=true</c>.</summary>
        [JsonProperty("descending")] public string Descending { get; set; }

        /// <summary>For "list": maximum number of items.</summary>
        [JsonProperty("limit")] public int? Limit { get; set; }

        /// <summary>For "bar": scale ends, numbers or arithmetic. Default 0 and 100.</summary>
        [JsonProperty("min")] public string Min { get; set; }
        [JsonProperty("max")] public string Max { get; set; }

        /// <summary>For "bar": warning color while this condition holds.</summary>
        [JsonProperty("warnWhen")] public string WarnWhen { get; set; }

        /// <summary>
        /// For "checklist": variable holding the unticked values, comma-separated, so an empty one means all ticked.
        /// For "list": makes rows selectable. The variable holds the selected values, comma-separated.
        /// A row's value is <see cref="Value"/>, default <c>{item.id}</c>.
        /// </summary>
        [JsonProperty("var")] public string Var { get; set; }

        /// <summary>For "list": action of a row tap, with <c>item</c> in its templates. Takes <c>set</c>, <c>link</c> or <c>planJobs</c>.</summary>
        [JsonProperty("click")] public SoftKeyDefinition Click { get; set; }

        /// <summary>For "checklist": items failing this condition show unticked and locked.</summary>
        [JsonProperty("enabledWhen")] public string EnabledWhen { get; set; }

        /// <summary>For "checklist": default 6.</summary>
        [JsonProperty("perRow")] public int? PerRow { get; set; }

        /// <summary>For "buttons": same keys as a soft key.</summary>
        [JsonProperty("buttons")] public List<SoftKeyDefinition> Buttons { get; set; }

        /// <summary>For "widget": built-in C# widget.</summary>
        [JsonProperty("name")] public string Name { get; set; }
    }

    public class ColumnDefinition
    {
        [JsonProperty("text")] public string Text { get; set; }
        [JsonProperty("width")] public float? Width { get; set; }
        [JsonProperty("flex")] public float? Flex { get; set; }
        [JsonProperty("align")] public string Align { get; set; }
        [JsonProperty("style")] public string Style { get; set; }
        [JsonProperty("when")] public string When { get; set; }

        /// <summary>Warning style while this condition holds for the item.</summary>
        [JsonProperty("warnWhen")] public string WarnWhen { get; set; }

        /// <summary>The header row shows when any column has one.</summary>
        [JsonProperty("header")] public string Header { get; set; }
    }

    public class SoftKeyDefinition
    {
        [JsonProperty("label")] public string Label { get; set; }

        /// <summary>Page to open, may contain placeholders: <c>station:{route.nextStationId}</c>.</summary>
        [JsonProperty("link")] public string Link { get; set; }

        /// <summary>Variables of the linked page to set before it opens.</summary>
        [JsonProperty("linkVars")] public Dictionary<string, string> LinkVars { get; set; }

        /// <summary>
        /// Page variables to set. Names starting with <c>settings.</c> set a tablet setting instead.
        /// Values are templates, e.g. <c>{"settings.brightness": "{settings.brightness + 10}"}</c>.
        /// In a buttons row, an item without <c>set</c>, <c>link</c> or <c>planJobs</c> shows as text.
        /// </summary>
        [JsonProperty("set")] public Dictionary<string, string> Set { get; set; }

        /// <summary>The key shows only while this condition holds.</summary>
        [JsonProperty("when")] public string When { get; set; }

        /// <summary>The key is disabled unless this condition holds.</summary>
        [JsonProperty("enabledWhen")] public string EnabledWhen { get; set; }

        /// <summary>Soft keys only: takes the rightmost free slot.</summary>
        [JsonProperty("right")] public bool Right { get; set; }

        /// <summary>Buttons rows only: drawn pressed while this condition holds.</summary>
        [JsonProperty("selectedWhen")] public string SelectedWhen { get; set; }

        /// <summary>
        /// Comma-separated job IDs to mark active for the shunting planner. Jobs not taken yet are planned as if taken.
        /// Runs before <see cref="Set"/> and <see cref="Link"/>.
        /// </summary>
        [JsonProperty("planJobs")] public string PlanJobs { get; set; }
    }

    public static class PageDefinitions
    {
        public static readonly string[] RowTypes = { "text", "heading", "divider", "field", "pairs", "bar", "buttons", "checklist", "list", "menu", "widget" };
        public static readonly string[] Styles = { "normal", "dim", "highlight", "warning", "bold", "large" };

        /// <summary>Widget names from PageWidgets, listed here so tests run without Unity.</summary>
        public static readonly string[] WidgetNames = { "timetable", "planner", "plannerStops", "jobs", "stopwatch", "tracks", "job", "forecast", "mediaPlayer", "mediaDevices", "radio", "pcAudio" };

        /// <summary>Returns null and fills <paramref name="problems"/> when the file is unusable.</summary>
        public static PageDefinition Parse(string json, string fileName, ICollection<string> knownWidgets, List<string> problems)
        {
            PageDefinition page;
            try
            {
                page = JsonConvert.DeserializeObject<PageDefinition>(json);
            }
            catch (JsonException ex)
            {
                problems.Add($"{fileName}: invalid JSON: {ex.Message}");
                return null;
            }

            if (page == null || string.IsNullOrWhiteSpace(page.Id))
            {
                problems.Add($"{fileName}: page has no \"id\"");
                return null;
            }

            page.Title = page.Title ?? page.Id;
            page.Rows = page.Rows ?? new List<RowDefinition>();
            page.SoftKeys = page.SoftKeys ?? new List<SoftKeyDefinition>();
            page.Vars = page.Vars ?? new Dictionary<string, string>();

            for (int i = 0; i < page.Rows.Count; i++)
                CheckRow(page.Rows[i], $"{fileName}: row {i + 1}", knownWidgets, problems);
            foreach (var key in page.SoftKeys.Where(k => string.IsNullOrEmpty(k.Label)))
                problems.Add($"{fileName}: soft key without \"label\"");
            return page;
        }

        private static void CheckRow(RowDefinition row, string where, ICollection<string> knownWidgets, List<string> problems)
        {
            if (row == null)
            {
                problems.Add($"{where}: empty row");
                return;
            }
            if (!RowTypes.Contains(row.Type))
                problems.Add($"{where}: unknown type \"{row.Type}\", expected one of {string.Join(", ", RowTypes)}");
            if (row.Style != null && !Styles.Contains(row.Style))
                problems.Add($"{where}: unknown style \"{row.Style}\"");
            if (row.Type == "list" && (string.IsNullOrEmpty(row.Source) || row.Columns == null || row.Columns.Count == 0))
                problems.Add($"{where}: a list needs \"source\" and \"columns\"");
            if (row.Type != "list" && (row.Filter != null || row.Sort != null || row.Descending != null || row.Limit != null))
                problems.Add($"{where}: \"filter\", \"sort\", \"descending\" and \"limit\" only work on lists");
            if (row.Type == "widget" && !knownWidgets.Contains(row.Name ?? ""))
                problems.Add($"{where}: unknown widget \"{row.Name}\", expected one of {string.Join(", ", knownWidgets)}");
            if (row.Type == "buttons" && (row.Buttons == null || row.Buttons.Count == 0 || row.Buttons.Any(b => b == null || string.IsNullOrEmpty(b.Label))))
                problems.Add($"{where}: a buttons row needs \"buttons\", each with a \"label\"");
            if (row.Type == "checklist" && (string.IsNullOrEmpty(row.Source) || string.IsNullOrEmpty(row.Var)
                || string.IsNullOrEmpty(row.Value) || string.IsNullOrEmpty(row.Text)))
                problems.Add($"{where}: a checklist needs \"source\", \"var\", \"value\" and \"text\"");
            if (row.Type == "bar" && string.IsNullOrEmpty(row.Value))
                problems.Add($"{where}: a bar needs \"value\"");
            if (row.Type == "pairs" && (row.Items == null || row.Items.Any(p => p == null || p.Count != 2)))
                problems.Add($"{where}: \"pairs\" needs items as [label, value] pairs");
            foreach (var column in row.Columns ?? new List<ColumnDefinition>())
                if (column.Style != null && !Styles.Contains(column.Style))
                    problems.Add($"{where}: unknown column style \"{column.Style}\"");
        }
    }
}
