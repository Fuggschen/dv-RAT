using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DV.Utils;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Shortest path to a station and the switches to set on the way. Stops come from <see cref="PlannerStopsWidget"/>.</summary>
    public class PlannerWidget : IPageWidget
    {
        private const string CurrentPosition = "Current position";

        private IReadOnlyList<KeyValuePair<string, string>> _stations;
        // Index 0 of the start picker is the player's train; station i is at i + 1.
        private int _from;
        private int _to;
        // True until the fake progress bar is full, which is after the real calculation ends.
        private bool _calculating;
        private float _progress;
        private RoutePlannerProvider.RouteResult _result;

        /// <summary>The bar runs this long per km of route, clamped, and slows with a weak radio signal.</summary>
        private const float SecondsPerKm = 1f;
        private const float MinSeconds = 2f;
        private const float MaxSeconds = 15f;
        private const float BarSteps = 20f;
        private PagedList _switches;
        private string _pageId;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            var root = parent;
            _pageId = pageId;
            _switches = null;
            var session = context.Tablet != null ? context.Tablet.Session : null;
            _stations = PlannerStations(context);
            if (_stations.Count == 0)
            {
                bool wire = context.Tablet != null && context.Tablet.PlanUnderWire;
                UIFactory.Height(UIFactory.Label(root, wire ? "No station has a track under the wire." : "No stations found on this map.",
                    Theme.FontBody, Theme.TextDim), Theme.RowHeight);
                if (wire)
                    UIFactory.Width(UIFactory.Button(UIFactory.Row(root, 40), WireCaption(session), () => ToggleWire(context, session)), 220);
                return;
            }

            if (session != null)
            {
                _from = session.PlannerFrom == null ? 0 : IndexOf(session.PlannerFrom) + 1;
                _to = Math.Max(0, IndexOf(session.PlannerTo));
            }
            if (_result == null && !_calculating && context.Tablet != null)
                _result = context.Tablet.LastRoute;
            // Drop a route for other stations, e.g. after the Jobs page filled in a plan.
            if (_result != null && _result.Success && session != null && !IsRouteFor(_result, session, context.Tablet.PlanUnderWire))
                _result = null;

            Picker(root, context, "From", _stations.Count + 1, () => _from, i => { _from = i; Remember(session); },
                i => i == 0 ? CurrentPosition : StationLabel(_stations[i - 1]));
            Picker(root, context, "To", _stations.Count, () => _to, i => { _to = i; Remember(session); }, i => StationLabel(_stations[i]));
            var stops = Stops(context);
            if (stops.Count > 0)
            {
                var via = UIFactory.Row(root);
                UIFactory.Width(UIFactory.Label(via, "Via", Theme.FontBody, Theme.TextDim), 60);
                UIFactory.Width(UIFactory.Label(via, string.Join(", ", stops.Select(Stations.Name)), Theme.FontBody, Theme.Text), 0, 1);
            }

            var row = UIFactory.Row(root, 40);
            UIFactory.Width(UIFactory.Button(row, "Calculate route", () => Calculate(context)), 220);
            if (CatenaryWires.Present)
                UIFactory.Width(UIFactory.Button(row, WireCaption(session), () => ToggleWire(context, session)), 180);
            UIFactory.Width(UIFactory.Label(row, StatusText(), Theme.FontBody,
                _result != null && !_result.Success ? Theme.Warning : Theme.TextDim), 0, 1);
            if (_calculating)
                ProgressBar.Create(root, () => Mathf.Floor(_progress * BarSteps) / BarSteps);

            UIFactory.Divider(root);
            if (_result == null || !_result.Success)
                return;

            row = UIFactory.Row(root);
            UIFactory.Pair(row, "Distance", Format.Distance(_result.TotalDistanceMeters), 90);
            UIFactory.Pair(row, "Est. time", Format.Duration(_result.TotalEstimatedTime), 90);
            UIFactory.Pair(row, "Reversals", _result.Reversals.ToString(), 100);

            _switches = new PagedList(root, stops.Count > 0 ? 6 : 7, "No facing switches on this route.", Theme.RowHeight,
                context.View(_pageId), "switches");
        }

        public void Refresh(PageContext context)
        {
            // Alignment is read live so the list follows switches the player throws.
            _switches?.SetRows(SwitchRows(_result), (cells, item) =>
            {
                if (item is Marker marker)
                    BuildMarkerRow(cells, marker);
                else
                    BuildSwitchRow(cells, (RoutePlannerProvider.JunctionAction)item, context);
            });
        }

        /// <summary>A start, stop, reversal or destination shown between the switches.</summary>
        private class Marker
        {
            public string Text;
            public float Distance;
            public bool Warning;
        }

        /// <summary>The switches in route order, with stops and reversals between them.</summary>
        private static IEnumerable<object> SwitchRows(RoutePlannerProvider.RouteResult result)
        {
            string start = result.StartStation == null ? CurrentPosition : Stations.Name(result.StartStation);
            yield return new Marker { Text = $"Start: {start}", Distance = 0f };
            var actions = result.JunctionActions;
            for (int i = 0; i <= actions.Count; i++)
            {
                foreach (var stop in result.Stops.Where(s => s.SwitchIndex == i))
                    yield return new Marker { Text = $"Stop: {Stations.Name(stop.StationId)}", Distance = stop.DistanceFromStart };
                // After the stops, since the train turns around when it leaves one.
                foreach (var reversal in result.ReversalPoints.Where(r => r.SwitchIndex == i))
                    yield return new Marker { Text = "Reverse direction", Distance = reversal.DistanceFromStart, Warning = true };
                if (i < actions.Count)
                    yield return actions[i];
            }
            yield return new Marker { Text = $"Destination: {Stations.Name(result.DestinationStation)}", Distance = result.TotalDistanceMeters };
        }

        private static void BuildMarkerRow(IRowCells cells, Marker marker)
        {
            var color = marker.Warning ? Theme.Warning : Theme.Highlight;
            cells.Label(Format.Distance(marker.Distance), 90, color: color, anchor: TextAnchor.MiddleRight);
            cells.Label(marker.Text, color: color, style: FontStyle.Bold);
        }

        /// <summary>The offered stations. With the wire option on, only those with a track under the wire.</summary>
        internal static IReadOnlyList<KeyValuePair<string, string>> PlannerStations(PageContext context)
        {
            var all = Stations.All();
            var tablet = context.Tablet;
            if (tablet == null || !tablet.PlanUnderWire) return all;
            return all.Where(s => tablet.StationUnderWire(s.Key)).ToList();
        }

        private static string WireCaption(TabletSession session) =>
            session != null && session.PlannerUnderWire ? "Wire only: on" : "Wire only: off";

        /// <summary>Drops the found route, which belongs to the other setting.</summary>
        private void ToggleWire(PageContext context, TabletSession session)
        {
            if (session == null || _calculating) return;
            session.PlannerUnderWire = !session.PlannerUnderWire;
            session.PlannerHasRoute = false;
            _result = null;
            context.Rebuild();
        }

        internal static bool IsRouteFor(RoutePlannerProvider.RouteResult route, TabletSession session, bool underWire) =>
            route.StartStation == session.PlannerFrom && route.DestinationStation == session.PlannerTo && route.UnderWire == underWire
            && route.Stops.Select(s => s.StationId).SequenceEqual(session.PlannerStops ?? new List<string>());

        private static List<string> Stops(PageContext context) =>
            context.Tablet?.Session.PlannerStops ?? new List<string>();

        private int IndexOf(string yardId)
        {
            for (int i = 0; i < _stations.Count; i++)
                if (_stations[i].Key == yardId) return i;
            return -1;
        }

        private void Remember(TabletSession session)
        {
            if (session == null) return;
            session.PlannerFrom = _from == 0 ? null : _stations[_from - 1].Key;
            session.PlannerTo = _stations[_to].Key;
        }

        private static string StationLabel(KeyValuePair<string, string> station) => $"{station.Value} ({station.Key})";

        private static void Picker(Transform root, PageContext context, string caption, int count,
            Func<int> get, Action<int> set, Func<int, string> label)
        {
            var row = UIFactory.Row(root, 40);
            UIFactory.Width(UIFactory.Label(row, caption, Theme.FontBody, Theme.TextDim), 60);
            UIFactory.Width(UIFactory.Button(row, "<", () => { set((get() + count - 1) % count); context.Rebuild(); }), 50);
            UIFactory.Width(UIFactory.Label(row, label(get()), Theme.FontBody, Theme.Text, TextAnchor.MiddleCenter), 0, 1);
            UIFactory.Width(UIFactory.Button(row, ">", () => { set((get() + 1) % count); context.Rebuild(); }), 50);
        }

        private string StatusText()
        {
            if (_calculating) return "Calculating...";
            if (_result == null) return "Pick a start and a destination.";
            if (!_result.Success) return _result.ErrorMessage;
            string route = $"Route to {Stations.Name(_result.DestinationStation)}";
            return _result.Stops.Count == 0 ? route : $"{route} with {_result.Stops.Count} {(_result.Stops.Count == 1 ? "stop" : "stops")}";
        }

        private void Calculate(PageContext context)
        {
            if (_calculating) return;

            string start = _from == 0 ? null : _stations[_from - 1].Key;
            string destination = _stations[_to].Key;
            var stops = Stops(context);
            // With stops, returning to the start is a valid round trip.
            if (start == destination && stops.Count == 0)
            {
                _result = new RoutePlannerProvider.RouteResult { ErrorMessage = "Start and destination are the same." };
                context.Rebuild();
                return;
            }
            if (context.Tablet == null || !context.Tablet.IsReady)
            {
                _result = new RoutePlannerProvider.RouteResult { ErrorMessage = "Tablet is not ready yet." };
                context.Rebuild();
                return;
            }

            _calculating = true;
            _progress = 0f;
            _result = null;
            context.Rebuild();
            var tablet = context.Tablet;
            tablet.CalculateRoute(start, stops.ToList(), destination, result =>
            {
                result = result ?? new RoutePlannerProvider.RouteResult { ErrorMessage = "No result received." };
                // A failure shows at once.
                if (result.Success && tablet != null)
                    tablet.StartCoroutine(FillBar(context, result));
                else
                    Show(context, result);
            });
        }

        /// <summary>Fakes a slow calculation: longer for long routes and weak signal. Runs on the tablet, so it survives leaving the page.</summary>
        private IEnumerator FillBar(PageContext context, RoutePlannerProvider.RouteResult result)
        {
            var tablet = context.Tablet;
            float baseSeconds = Mathf.Clamp(result.TotalDistanceMeters / 1000f * SecondsPerKm, MinSeconds, MaxSeconds);
            while (_progress < 1f)
            {
                float strength = tablet.RadioSignal != null ? tablet.RadioSignal.Strength : 1f;
                _progress = SignalLoad.Advance(_progress, Time.deltaTime, strength, baseSeconds);
                yield return null;
            }
            Show(context, result);
        }

        private void Show(PageContext context, RoutePlannerProvider.RouteResult result)
        {
            _calculating = false;
            _result = result;
            if (context.Tablet != null)
                context.Tablet.Session.PlannerHasRoute = _result.Success;
            if (context.Navigator.Current == _pageId)
                context.Rebuild();
        }

        private void BuildSwitchRow(IRowCells cells, RoutePlannerProvider.JunctionAction action, PageContext context)
        {
            var junction = action.Junction;
            bool aligned = junction != null && junction.selectedBranch == action.RequiredBranchIndex;
            cells.Label(Format.Distance(action.DistanceFromStart), 90, color: Theme.Text, anchor: TextAnchor.MiddleRight);
            cells.Label($"{action.JunctionLabel}  branch {action.RequiredBranchIndex + 1}");
            if (aligned || junction == null)
            {
                cells.Label("set", ThrowWidth, color: Theme.TextDim);
                return;
            }

            // Permission is part of the row's look, so a newly bought license unlocks the button on the next refresh.
            bool allowed = CanThrow(junction);
            cells.Button("Throw", () =>
            {
                if (!CanThrow(junction)) return;
                junction.Switch(Junction.SwitchMode.REGULAR, (byte)action.RequiredBranchIndex);
                // Refresh keeps the list on its screen.
                Refresh(context);
            }, size: Theme.FontSmall, color: allowed ? Theme.Warning : Theme.TextDim, enabled: allowed,
                fixedSize: new Vector2(ThrowWidth, Theme.RowHeight - 2));
        }

        /// <summary>Needs the Dispatcher license, like the game's junction map, and switching allowed (the tutorial limits it).</summary>
        private static bool CanThrow(Junction junction)
        {
            var licenses = SingletonBehaviour<LicenseManager>.Instance;
            return licenses != null && licenses.IsGeneralLicenseAcquired(GeneralLicenseType.Dispatcher1.ToV2())
                && SingletonBehaviour<JunctionSwitcherManager>.Instance.IsSwitchingAllowed(junction);
        }

        private const float ThrowWidth = 80f;

        public IEnumerable<SoftKey> SoftKeys(PageContext context) => _switches?.SoftKeys() ?? Enumerable.Empty<SoftKey>();
    }
}
