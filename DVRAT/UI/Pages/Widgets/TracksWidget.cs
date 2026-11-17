using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DVRAT
{
    /// <summary>The nearest station's tracks as bars grouped by sub-yard. A tap on a track lists its cars, a tap on a car shows its job.</summary>
    public class TracksWidget : IPageWidget, IBackStep
    {
        private const float RowHeight = 22f;
        private const float IdWidth = 80f;
        // Wide enough for "1250/1250 m".
        private const float FreeWidth = 100f;
        private const float LegendHeight = 20f;
        private const float SwatchSize = 12f;
        // Share of the bar's height the cars fill.
        private const float CarHeight = 0.6f;
        private const float CarGap = 1f;

        // Taller than track rows because each car row is a tap target.
        private const float CarRowHeight = 28f;
        private const float JobIdWidth = 85f;
        private const float JobTypeWidth = 35f;

        private PagedList _list;

        // Open track, job and the station the track was opened at. Kept in the view state so every tablet copy
        // shows the same, and they stay open on other pages. A missing key shows the level above.
        private const string TrackKey = "track";
        private const string JobKey = "job";
        private const string StationKey = "station";
        private Dictionary<string, string> _state = new Dictionary<string, string>();
        // What Build showed. Track and job views show only at the station they were opened at.
        private bool _showsTrack, _showsJob;

        private string Get(string key) => _state.TryGetValue(key, out string value) ? value : null;

        private Text _summary;
        private RectTransform _bar;

        private JobView _jobView;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            _list = null;
            _jobView = null;
            _state = context.View(pageId) ?? new Dictionary<string, string>();
            bool atStation = AtOpenedStation(context);
            _showsJob = atStation && Get(JobKey) != null;
            _showsTrack = atStation && !_showsJob && Get(TrackKey) != null;
            if (_showsJob)
            {
                _jobView = new JobView(parent, rows, _state, Get(JobKey));
                return;
            }
            if (_showsTrack)
            {
                BuildTrack(parent, rows);
                return;
            }
            Legend(parent);
            ColumnHeaders(parent);
            _list = new PagedList(parent, rows ?? 0, "No station tracks nearby.", RowHeight, _state, "tracks");
        }

        // With no nearest station the open view stays, so gaps in the data do not close it.
        private bool AtOpenedStation(PageContext context)
        {
            string station = context.World?.NearestStationId;
            return station == null || station == Get(StationKey);
        }

        public void Refresh(PageContext context)
        {
            // At another station they stay open for players still at the first one.
            bool atStation = AtOpenedStation(context);
            if ((_showsTrack || _showsJob) && !atStation || !(_showsTrack || _showsJob) && atStation && Get(TrackKey) != null)
            {
                context.Rebuild();
                return;
            }

            if (_showsJob)
                _jobView.Refresh(Get(JobKey));
            else if (_showsTrack)
                RefreshTrack(context);
            else
                RefreshTracks(context);
        }

        public bool CanGoBack => _showsJob || _showsTrack;

        public void GoBack(PageContext context)
        {
            if (_showsJob)
                _state.Remove(JobKey);
            else
            {
                _state.Remove(TrackKey);
                _state.Remove(StationKey);
            }
            context.Rebuild();
        }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            var keys = _jobView != null ? _jobView.SoftKeys() : _list?.SoftKeys() ?? Enumerable.Empty<SoftKey>();
            foreach (var key in keys)
                yield return key;
        }

        private static TrackSpace Track(PageContext context, string id) =>
            context.World?.StationTracks.FirstOrDefault(t => t.Id == id);

        #region All tracks

        private void RefreshTracks(PageContext context)
        {
            var tracks = context.World?.StationTracks ?? new List<TrackSpace>();
            var groups = tracks
                .GroupBy(t => t.Group)
                .OrderBy(g => g.Key, System.StringComparer.Ordinal)
                .Select(g => (IReadOnlyList<System.Action<IRowCells>>)
                    new System.Action<IRowCells>[] { cells => GroupHeading(cells, g.Key) }
                    .Concat(g.Select(t => (System.Action<IRowCells>)(cells => TrackRow(cells, t, context))))
                    .ToList());
            _list.SetGroups(groups);
        }

        private static void GroupHeading(IRowCells cells, string group)
        {
            string text = string.IsNullOrEmpty(group) ? "Other tracks" : $"Sub-yard {group}";
            cells.Label(text, color: Theme.Highlight, anchor: TextAnchor.LowerLeft, style: FontStyle.Bold);
        }

        private void TrackRow(IRowCells cells, TrackSpace track, PageContext context)
        {
            string id = track.Id;
            cells.Clickable(() =>
            {
                _state[TrackKey] = id;
                _state[StationKey] = context.World?.NearestStationId;
                context.Rebuild();
            });
            cells.Label(track.Id, IdWidth, size: Theme.FontSmall, color: Theme.Text);
            // Tenth of a metre is the bar's resolution, finer would redraw for nothing.
            cells.Custom($"{track.RailLength:F1}|" + string.Join(",", track.Cars.Select(c => $"{c.From:F1}-{c.To:F1}:{c.Kind}")), row =>
            {
                var bar = UIFactory.Rect("Track", row);
                UIFactory.Width(bar, 0, 1);
                DrawCars(bar, track);
            });
            cells.Label($"{track.Length:F0}/{track.Free:F0} m", FreeWidth, size: Theme.FontSmall, color: Theme.TextDim,
                anchor: TextAnchor.MiddleRight);
        }

        private static void ColumnHeaders(Transform parent)
        {
            var row = UIFactory.Row(parent, RowHeight, name: "Headers");
            UIFactory.Width(UIFactory.Label(row, "Track", Theme.FontSmall, Theme.TextDim), IdWidth);
            UIFactory.Width(UIFactory.Label(row, "Cars", Theme.FontSmall, Theme.TextDim), 0, 1);
            UIFactory.Width(UIFactory.Label(row, "Total/free", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), FreeWidth);
        }

        /// <summary>Draws <paramref name="track"/> into <paramref name="bar"/>, replacing earlier drawing.</summary>
        private static void DrawCars(RectTransform bar, TrackSpace track)
        {
            var rail = bar.GetComponent<Image>();
            if (rail == null)
                rail = bar.gameObject.AddComponent<Image>();
            rail.color = Theme.Panel;
            rail.raycastTarget = false;
            for (int i = bar.childCount - 1; i >= 0; i--)
            {
                var child = bar.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            if (track.RailLength > 0f)
                foreach (var car in track.Cars)
                    Car(bar, car, track.RailLength);
        }

        private static void Car(RectTransform bar, TrackCar car, float railLength)
        {
            const float margin = (1f - CarHeight) / 2f;
            var rect = UIFactory.Rect("Car", bar);
            rect.anchorMin = new Vector2(Mathf.Clamp01(car.From / railLength), margin);
            rect.anchorMax = new Vector2(Mathf.Clamp01(car.To / railLength), 1f - margin);
            rect.offsetMin = new Vector2(CarGap, 0f);
            rect.offsetMax = new Vector2(-CarGap, 0f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ColorOf(car.Kind);
            image.raycastTarget = false;
        }

        private static Color ColorOf(TrackCarKind kind)
        {
            switch (kind)
            {
                case TrackCarKind.Train: return Theme.Highlight;
                case TrackCarKind.Planned: return Theme.Done;
                case TrackCarKind.Loco: return Theme.Accent;
                default: return Theme.TextDim;
            }
        }

        private static void Legend(Transform parent)
        {
            var row = UIFactory.Row(parent, LegendHeight, 6, "Legend");
            Entry(row, Theme.Highlight, "Your train");
            Entry(row, Theme.Done, "Planned jobs");
            Entry(row, Theme.Accent, "Other locos");
            Entry(row, Theme.TextDim, "Other cars");
            Entry(row, Theme.Panel, "Free");
            UIFactory.Width(UIFactory.Rect("Spacer", row), 0, 1);
        }

        private static void Entry(RectTransform row, Color color, string text)
        {
            var cell = UIFactory.Rect("Swatch", row);
            UIFactory.Width(cell, SwatchSize);
            var swatch = UIFactory.Rect("Color", cell);
            swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 0.5f);
            swatch.sizeDelta = new Vector2(SwatchSize, SwatchSize);
            var image = swatch.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            var label = UIFactory.Label(row, text, Theme.FontSmall, Theme.Text);
            UIFactory.Width(label, label.preferredWidth + 10f);
        }

        #endregion

        #region One track

        private void BuildTrack(RectTransform parent, int? rows)
        {
            var title = UIFactory.Row(parent, RowHeight, name: "Title");
            UIFactory.Width(UIFactory.Label(title, $"Track {Get(TrackKey)}", Theme.FontBody, Theme.Highlight, TextAnchor.MiddleLeft, FontStyle.Bold), 0, 1);
            _summary = UIFactory.Width(UIFactory.Label(title, "", Theme.FontSmall, Theme.TextDim, TextAnchor.MiddleRight), 0, 1);

            var barRow = UIFactory.Row(parent, RowHeight, name: "Bar");
            _bar = UIFactory.Rect("Track", barRow);
            UIFactory.Width(_bar, 0, 1);

            var headers = UIFactory.Row(parent, RowHeight, name: "Headers");
            UIFactory.Cell(headers, "Car", IdWidth, Theme.TextDim);
            UIFactory.Cell(headers, "Type", 0, Theme.TextDim);
            UIFactory.Cell(headers, "Cargo", 0, Theme.TextDim);
            UIFactory.Cell(headers, "Job", JobIdWidth, Theme.TextDim);
            UIFactory.Cell(headers, "", JobTypeWidth, Theme.TextDim);
            UIFactory.Cell(headers, "To", IdWidth, Theme.TextDim);
            UIFactory.Cell(headers, "Next task", 0, Theme.TextDim);
            _list = new PagedList(parent, rows ?? 0, "No cars on this track.", CarRowHeight, _state, "cars", Get(TrackKey));
        }

        private void RefreshTrack(PageContext context)
        {
            var track = Track(context, Get(TrackKey));
            if (track == null)
            {
                _summary.text = "Not at the shown station";
                _list.SetRows(new List<TrackCar>(), (cells, car) => { });
                return;
            }

            _summary.text = $"{Format.Count(track.Cars.Count, "car")}  |  {track.Length:F0} m, {track.Free:F0} m free";
            DrawCars(_bar, track);
            var jobs = track.Cars.Select(c => c.JobId).Where(id => id != null).Distinct()
                .ToDictionary(id => id, id => WorldDataProvider.FindJob(id, out _));
            _list.SetRows(track.Cars, (cells, car) => CarRow(cells, car, car.JobId != null ? jobs[car.JobId] : null, context));
        }

        private void CarRow(IRowCells cells, TrackCar car, JobInfo job, PageContext context)
        {
            if (job != null)
            {
                string id = job.JobId;
                cells.Clickable(() =>
                {
                    _state[JobKey] = id;
                    context.Rebuild();
                });
            }
            // Null when all the car's tasks are done.
            var jobCar = job?.Cars.FirstOrDefault(c => c.Id == car.Id);
            cells.Cell(car.Id, IdWidth, car.Kind == TrackCarKind.Other ? Theme.Text : ColorOf(car.Kind));
            cells.Cell(car.Name ?? "", 0, Theme.Text);
            cells.Cell(car.Cargo ?? "Empty", 0, car.Cargo != null ? Theme.Text : Theme.TextDim);
            cells.Cell(job?.JobId ?? "No job", JobIdWidth, job == null ? Theme.TextDim : job.IsActive ? Theme.Done : Theme.Text);
            cells.Cell(job?.JobTypeShort ?? "", JobTypeWidth, Theme.Text);
            cells.Cell(job == null ? "" : jobCar?.FinalTrack ?? job.DestinationTrackId ?? "", IdWidth, Theme.Text);
            cells.Cell(job == null ? "" : jobCar?.NextTask ?? "Done", 0, Theme.Text);
        }

        #endregion
    }
}
