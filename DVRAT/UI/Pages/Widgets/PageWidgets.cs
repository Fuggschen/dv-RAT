using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>A piece of a JSON page that needs C# logic, placed with <c>{"type": "widget", "name": "..."}</c>.</summary>
    public interface IPageWidget
    {
        /// <param name="parent">Column to build into. It takes the height the other rows leave.</param>
        /// <param name="rows">Optional row count from the JSON, for tables.</param>
        void Build(RectTransform parent, PageContext context, string pageId, int? rows);

        void Refresh(PageContext context);

        IEnumerable<SoftKey> SoftKeys(PageContext context);
    }

    /// <summary>A widget with inner views that the Back key leaves one at a time before leaving the page.</summary>
    public interface IBackStep
    {
        bool CanGoBack { get; }

        void GoBack(PageContext context);
    }

    public static class PageWidgets
    {
        public static ICollection<string> Names => PageDefinitions.WidgetNames;

        /// <summary>A new widget for a name in <see cref="PageDefinitions.WidgetNames"/>, otherwise null.</summary>
        /// <param name="variable">The row's <c>var</c>, for widgets that read a page variable.</param>
        public static IPageWidget Create(string name, string variable)
        {
            switch (name)
            {
                case "timetable": return new TimetableWidget();
                case "planner": return new PlannerWidget();
                case "plannerStops": return new PlannerStopsWidget();
                case "jobs": return new JobsWidget();
                case "stopwatch": return new StopwatchWidget();
                case "tracks": return new TracksWidget();
                case "job": return new JobWidget(variable);
                case "forecast": return new ForecastWidget();
                case "mediaPlayer": return new MediaPlayerWidget();
                case "mediaDevices": return new MediaDevicesWidget();
                case "radio": return new RadioWidget();
                case "pcAudio": return new PcAudioWidget();
                default: return null;
            }
        }
    }
}
