using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>The route planner's stops, driven in list order. <see cref="PlannerWidget"/> reads them from the session.</summary>
    public class PlannerStopsWidget : IPageWidget
    {
        private IReadOnlyList<KeyValuePair<string, string>> _stations;
        private PagedList _list;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _list = null;
            var session = context.Tablet != null ? context.Tablet.Session : null;
            _stations = PlannerWidget.PlannerStations(context);
            if (session == null || _stations.Count == 0)
            {
                string text = context.Tablet != null && context.Tablet.PlanUnderWire
                    ? "No station has a track under the wire." : "No stations found on this map.";
                UIFactory.Height(UIFactory.Label(parent, text, Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                return;
            }

            var row = UIFactory.Row(parent, 40);
            UIFactory.Width(UIFactory.Button(row, "Add stop", () =>
            {
                // The destination is the likeliest neighbour of a new last stop.
                session.PlannerStops.Add(session.PlannerTo ?? _stations[0].Key);
                context.Rebuild();
            }), 160);
            UIFactory.Width(UIFactory.Label(row, $"To {Stations.Name(session.PlannerTo)}", Theme.FontBody, Theme.TextDim), 0, 1);

            UIFactory.Divider(parent);
            _list = new PagedList(parent, 0, "No stops, the route goes straight to the destination.", 40, context.View(pageId), "stops");
        }

        public void Refresh(PageContext context)
        {
            var session = context.Tablet != null ? context.Tablet.Session : null;
            if (_list == null || session == null) return;
            _list.SetRows(Enumerable.Range(0, session.PlannerStops.Count), (cells, i) => BuildStopRow(cells, i, session, context));
        }

        private void BuildStopRow(IRowCells cells, int index, TabletSession session, PageContext context)
        {
            var stops = session.PlannerStops;
            int station = IndexOf(stops[index]);
            string does = $"{index}:{stops[index]}";
            cells.Label($"{index + 1}.", 40, color: Theme.TextDim, anchor: TextAnchor.MiddleRight);
            // Refresh redraws only the rows, so the list keeps its screen.
            cells.Button("<", () =>
            {
                stops[index] = _stations[(station + _stations.Count - 1) % _stations.Count].Key;
                Refresh(context);
            }, 50, does: does);
            cells.Label(station >= 0 ? StationLabel(_stations[station]) : Stations.Name(stops[index]),
                color: Theme.Text, anchor: TextAnchor.MiddleCenter);
            cells.Button(">", () =>
            {
                stops[index] = _stations[(station + 1) % _stations.Count].Key;
                Refresh(context);
            }, 50, does: does);
            cells.Button("Remove", () =>
            {
                stops.RemoveAt(index);
                context.Rebuild();
            }, 90, Theme.FontSmall, does: does);
        }

        private int IndexOf(string yardId)
        {
            for (int i = 0; i < _stations.Count; i++)
                if (_stations[i].Key == yardId) return i;
            return -1;
        }

        private static string StationLabel(KeyValuePair<string, string> station) => $"{station.Value} ({station.Key})";

        public IEnumerable<SoftKey> SoftKeys(PageContext context) => _list?.SoftKeys() ?? Enumerable.Empty<SoftKey>();
    }
}
