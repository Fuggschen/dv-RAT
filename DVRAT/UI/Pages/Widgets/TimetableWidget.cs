using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>EbuLa-style table of the route ahead. Reads bottom to top, with passed entries below the position mark.</summary>
    public class TimetableWidget : IPageWidget
    {
        private const int DefaultRows = 11;
        private const float SpeedWidth = 70f;
        private const float DistanceWidth = 90f;
        private const float GradeWidth = 80f;
        private const float ArrivalWidth = 70f;
        private const float BarWidth = 16f;
        private const float DiamondSize = 11f;
        private const float MarkerHeight = 14f;
        private const float TableRowHeight = Theme.RowHeight + 2f;

        /// <summary>Passed entries shown under the position mark.</summary>
        private const int PassedRows = 2;

        private int _rows = DefaultRows;
        // Scroll offset ahead, kept in the view state so every tablet copy shows the same.
        private const string OffsetKey = "offset";
        private Dictionary<string, string> _state = new Dictionary<string, string>();
        // The stored offset clamped to the current table length.
        private int _offset;

        private int StoredOffset
        {
            get => _state.TryGetValue(OffsetKey, out string saved) && int.TryParse(saved, out int offset) ? offset : 0;
            set
            {
                if (value > 0) _state[OffsetKey] = value.ToString();
                else _state.Remove(OffsetKey);
            }
        }

        // Built once, refresh only changes texts. Recreating rows each refresh made the frame take three times as long.
        private readonly List<Cells> _tableRows = new List<Cells>();
        private readonly List<Cells> _passedRows = new List<Cells>();

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _rows = Mathf.Max(1, rows ?? DefaultRows);
            _state = context.View(pageId) ?? new Dictionary<string, string>();

            var header = UIFactory.Row(parent, 20);
            UIFactory.Width(UIFactory.Rect("Gap", header), BarWidth);
            new Cells(header).Show(new Entry("V", "km", "Location", "Arr.", "%", Theme.TextDim, Theme.FontSmall, FontStyle.Normal));
            UIFactory.Divider(parent);

            // No spacing, so the bar runs through without gaps.
            var table = UIFactory.Column(parent, 0, "Table");
            UIFactory.Height(table, _rows * TableRowHeight);
            _tableRows.Clear();
            for (int i = 0; i < _rows; i++)
                _tableRows.Add(TableRow(table));

            var marker = UIFactory.Row(parent, MarkerHeight, 8, "Position");
            Bar(marker, true);
            var lineCell = UIFactory.Rect("Line", marker);
            UIFactory.Width(lineCell, 0, 1);
            var line = UIFactory.Rect("Stroke", lineCell);
            line.anchorMin = new Vector2(0f, 0.5f);
            line.anchorMax = new Vector2(1f, 0.5f);
            line.sizeDelta = new Vector2(0f, 3f);
            Paint(line);

            var passedTable = UIFactory.Column(parent, 0, "Passed");
            UIFactory.Height(passedTable, PassedRows * TableRowHeight);
            _passedRows.Clear();
            for (int i = 0; i < PassedRows; i++)
                _passedRows.Add(TableRow(passedTable));
        }

        public void Refresh(PageContext context)
        {
            var route = context.Route;
            bool onTrack = route != null && !string.IsNullOrEmpty(route.CurrentTrackId);
            var entries = onTrack
                ? Entries(route, context.World, ShowAspects(context), TrainClearLength(context), MarkMisaligned(context))
                : new List<Entry>();
            _offset = Mathf.Clamp(StoredOffset, 0, Mathf.Max(0, entries.Count - _rows));
            var visible = entries.Skip(_offset).Take(_rows).ToList();

            // Fill from the bottom: empty rows go on top, the nearest entry sits above the marker.
            int empty = _rows - visible.Count;
            for (int i = 0; i < _rows; i++)
                _tableRows[i].Show(i < empty ? Entry.Blank : visible[_rows - 1 - i]);

            // Latest passed entry first, and only while the view is at the train's position.
            var passed = onTrack && _offset == 0 ? PassedEntries(route, ShowAspects(context)) : new List<Entry>();
            var shown = passed.Skip(Mathf.Max(0, passed.Count - PassedRows)).Reverse().ToList();
            for (int i = 0; i < PassedRows; i++)
                _passedRows[i].Show(i < shown.Count ? shown[i] : Entry.Blank);
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            var route = context.Route;
            int count = route == null || string.IsNullOrEmpty(route.CurrentTrackId) ? 0
                : Entries(route, context.World, ShowAspects(context), TrainClearLength(context), MarkMisaligned(context)).Count;
            int offset = Mathf.Clamp(StoredOffset, 0, Mathf.Max(0, count - _rows));
            // The table reads bottom to top, so up is farther ahead.
            yield return new SoftKey(Icons.Label(Icons.Up), offset + _rows < count ? () => { StoredOffset = offset + _rows - 1; context.Rebuild(); } : (System.Action)null);
            yield return new SoftKey(Icons.Label(Icons.Down), offset > 0 ? () => { StoredOffset = Mathf.Max(0, offset - (_rows - 1)); context.Rebuild(); } : (System.Action)null);
            yield return new SoftKey(Icons.Label(Icons.ToBottom), offset > 0 ? () => { StoredOffset = 0; context.Rebuild(); } : (System.Action)null);
        }

        private static bool ShowAspects(PageContext context) =>
            context.Tablet == null || context.Tablet.Session.Setting("showSignalAspects") != "false";

        /// <summary>Setting "markMisalignedSwitches": switches set against the planned route are red.</summary>
        private static bool MarkMisaligned(PageContext context) =>
            context.Tablet == null || context.Tablet.Session.Setting("markMisalignedSwitches") != "false";

        /// <summary>Train length for the ¥ rows. 0 when "showTrainClear" is off or the length is unknown.</summary>
        private static float TrainClearLength(PageContext context)
        {
            if (context.Tablet != null && context.Tablet.Session.Setting("showTrainClear") == "false") return 0f;
            return context.Train?.TotalLength ?? 0f;
        }

        /// <summary>Where the limit rises for the whole train, once its end has left every lower section.</summary>
        private static IEnumerable<TrackEvent> TrainClearEvents(RouteData route, float trainLength) =>
            RouteProfile.TrainClearPoints(route.SpeedSectionsBehind, route.SpeedSections, trainLength, route.ScannedMeters)
                .Select(point => new TrackEvent { EventType = TrackEventType.TrainClear, DistanceMeters = point.at, Value = point.limit });

        private static List<Entry> Entries(RouteData route, WorldData world, bool showAspects, float trainLength,
            bool markMisaligned)
        {
            // OrderBy is stable, so a ¥ row comes after an event at the same spot.
            var events = route.UpcomingEvents.Concat(TrainClearEvents(route, trainLength)).OrderBy(e => e.DistanceMeters)
                .Where(e => e.DistanceMeters > 0 || e.EventType == TrackEventType.Station);
            return Rows(events, route, world, showAspects, markMisaligned, (int)route.SpeedLimitKmh, true);
        }

        /// <summary>Rows for passed events, oldest first. Stations the train is still at are excluded, they show above the marker.</summary>
        private static List<Entry> PassedEntries(RouteData route, bool showAspects)
        {
            var here = new HashSet<string>(route.UpcomingEvents
                .Where(e => (e.EventType == TrackEventType.Station || e.EventType == TrackEventType.Track) && e.DistanceMeters <= 0)
                .Select(e => e.StationId));
            var events = route.PassedEvents.Where(e =>
                !((e.EventType == TrackEventType.Station || e.EventType == TrackEventType.Track) && here.Contains(e.StationId)));
            // A passed switch is not marked.
            return Rows(events, route, null, showAspects, false, 0, false);
        }

        private static List<Entry> Rows(IEnumerable<TrackEvent> events, RouteData route, WorldData world,
            bool showAspects, bool markMisaligned, int previousLimit, bool withArrival)
        {
            var entries = new List<Entry>();
            var list = events.ToList();
            // A station whose tracks are listed gets no row of its own.
            var stationsWithTracks = new HashSet<string>(list
                .Where(e => e.EventType == TrackEventType.Track && e.StationId != null).Select(e => e.StationId));
            foreach (var ev in list)
            {
                string km = (ev.DistanceMeters / 1000f).ToString("0.0");
                switch (ev.EventType)
                {
                    case TrackEventType.SpeedLimitChange:
                        int limit = (int)ev.Value;
                        var color = limit < previousLimit ? Theme.Warning : Theme.Text;
                        previousLimit = limit;
                        entries.Add(new Entry(limit.ToString(), km, "", "", "", color, Theme.FontBody, FontStyle.Bold));
                        break;
                    case TrackEventType.GradeChange:
                        entries.Add(new Entry("", km, "", "", ev.Value.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture), Theme.Text, Theme.FontBody, FontStyle.Normal));
                        break;
                    case TrackEventType.Station:
                    {
                        if (stationsWithTracks.Contains(ev.StationId)) break;
                        string name = ev.Name;
                        string arrival = withArrival ? ArrivalText(route, world, ev.DistanceMeters) : "";
                        entries.Add(new Entry("", km, name, arrival, "", Theme.Highlight, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.Track:
                    {
                        string name = $"{Stations.Name(ev.StationId)}  > {ev.Name}";
                        string arrival = withArrival ? ArrivalText(route, world, ev.DistanceMeters) : "";
                        entries.Add(new Entry("", km, name, arrival, "", Theme.Highlight, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.Signal:
                    {
                        string signal = !showAspects ? ev.Name : ev.Aspect == null ? $"{ev.Name}: off" : $"{ev.Name}: {ev.Aspect}";
                        bool warn = showAspects && ev.Restrictive;
                        entries.Add(new Entry("", km, signal, "", "", warn ? Theme.Warning : Theme.Text,
                            Theme.FontSmall, warn ? FontStyle.Bold : FontStyle.Normal));
                        break;
                    }
                    case TrackEventType.TrainClear:
                    {
                        string clear = $"Train clear, {ev.Value:F0} km/h";
                        entries.Add(new Entry("\u00A5", km, clear, "", "", Theme.Text, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.NeutralBegin:
                    case TrackEventType.WireEnd:
                    {
                        string name = ev.Name;
                        entries.Add(new Entry("", km, name, "", "", Theme.Warning, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.NeutralEnd:
                    case TrackEventType.WireBegin:
                    {
                        string name = ev.Name;
                        entries.Add(new Entry("", km, name, "", "", Theme.Text, Theme.FontBody, FontStyle.Normal));
                        break;
                    }
                    case TrackEventType.TrackDamage:
                    {
                        // The restriction's limit goes in the speed column, like a sign.
                        int restricted = (int)ev.Value;
                        string name = ev.Name;
                        previousLimit = restricted;
                        entries.Add(new Entry(restricted.ToString(), km, name, "", "", Theme.Warning, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.TrackDamageEnd:
                    {
                        int after = (int)ev.Value;
                        string name = ev.Name;
                        if (after > 0)
                            previousLimit = after;
                        entries.Add(new Entry(after > 0 ? after.ToString() : "", km, name, "", "", Theme.Text, Theme.FontBody, FontStyle.Bold));
                        break;
                    }
                    case TrackEventType.Junction:
                        string text = $"Switch {ev.Name}";
                        // Junction Maintenance mod: a damaged switch may throw itself under the train.
                        if (ev.Switch?.Repairing == true)
                            text += "  under repair";
                        else if (ev.Switch?.DamagePercent > 0)
                            text += $"  damaged {ev.Switch.DamagePercent} %{(ev.Switch.Locked ? ", locked" : "")}";
                        if (markMisaligned && ev.Misaligned || ev.Switch?.DamagePercent > 0 || ev.Switch?.Locked == true)
                            entries.Add(new Entry("", km, text, "", "", Theme.Warning, Theme.FontSmall, FontStyle.Bold));
                        else
                            entries.Add(new Entry("", km, text, "", "", Theme.TextDim, Theme.FontSmall, FontStyle.Normal));
                        break;
                    default:
                        string label = ev.Name;
                        entries.Add(new Entry("", km, label, "", "", Theme.Warning, Theme.FontBody, FontStyle.Bold));
                        break;
                }
            }
            return entries;
        }

        /// <summary>Estimated arrival in game time. Empty without a game clock.</summary>
        private static string ArrivalText(RouteData route, WorldData world, float distance) =>
            PageBindings.Arrival(route, world, distance)?.ToString("HH:mm") ?? "";

        /// <summary>The texts of one row. Speed and location carry the entry's color and style.</summary>
        private readonly struct Entry
        {
            public static readonly Entry Blank = new Entry("", "", "", "", "", Theme.Text, Theme.FontBody, FontStyle.Normal);

            public readonly string Speed, Km, Location, Arrival, Grade;
            public readonly Color Color;
            public readonly int Size;
            public readonly FontStyle Style;

            public Entry(string speed, string km, string location, string arrival, string grade, Color color, int size, FontStyle style)
            {
                Speed = speed;
                Km = km;
                Location = location;
                Arrival = arrival;
                Grade = grade;
                Color = color;
                Size = size;
                Style = style;
            }
        }

        /// <summary>The labels of one row. Unity's Text setters ignore unchanged values, so showing the same entry leaves the canvas clean.</summary>
        private sealed class Cells
        {
            private readonly Text _speed, _km, _location, _arrival, _grade;

            public Cells(RectTransform row)
            {
                _speed = UIFactory.Width(UIFactory.Label(row, "", anchor: TextAnchor.MiddleRight), SpeedWidth);
                _km = UIFactory.Width(UIFactory.Label(row, "", anchor: TextAnchor.MiddleRight), DistanceWidth);
                _location = UIFactory.Width(UIFactory.Label(row, "", anchor: TextAnchor.MiddleLeft), 0, 1);
                _arrival = UIFactory.Width(UIFactory.Label(row, "", anchor: TextAnchor.MiddleRight), ArrivalWidth);
                _grade = UIFactory.Width(UIFactory.Label(row, "", anchor: TextAnchor.MiddleRight), GradeWidth);
            }

            public void Show(Entry entry)
            {
                Set(_speed, entry.Speed, entry.Color, entry.Size, entry.Style);
                Set(_km, entry.Km, Theme.Text, entry.Size, FontStyle.Normal);
                Set(_location, entry.Location, entry.Color, entry.Size, entry.Style);
                Set(_arrival, entry.Arrival, Theme.Text, entry.Size, FontStyle.Normal);
                Set(_grade, entry.Grade, Theme.Text, entry.Size, FontStyle.Normal);
            }

            private static void Set(Text label, string text, Color color, int size, FontStyle style)
            {
                label.text = text;
                label.color = color;
                label.fontSize = size;
                label.fontStyle = style;
            }
        }

        private static Cells TableRow(RectTransform table)
        {
            var row = UIFactory.Row(table, TableRowHeight);
            Bar(row, false);
            return new Cells(row);
        }

        /// <summary>The bar cell of a row: a vertical line, with a diamond at the train's position.</summary>
        private static void Bar(RectTransform row, bool diamond)
        {
            var cell = UIFactory.Rect("Bar", row);
            UIFactory.Width(cell, BarWidth);
            var line = UIFactory.Rect("Line", cell);
            line.anchorMin = new Vector2(0.5f, 0f);
            line.anchorMax = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(Theme.LineWidth, 0f);
            Paint(line);
            if (!diamond) return;

            var mark = UIFactory.Rect("Diamond", cell);
            mark.anchorMin = mark.anchorMax = new Vector2(0.5f, 0.5f);
            mark.sizeDelta = new Vector2(DiamondSize, DiamondSize);
            mark.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Paint(mark);
        }

        private static void Paint(RectTransform rect)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Theme.Text;
            image.raycastTarget = false;
        }
    }
}
