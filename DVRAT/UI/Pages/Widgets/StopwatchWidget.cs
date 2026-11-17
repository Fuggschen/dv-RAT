using System;
using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>A stopwatch kept in the tablet session, so it runs across pages and power cycles and is saved with the game.</summary>
    public class StopwatchWidget : IPageWidget
    {
        // Used without a tablet, e.g. in the Unity editor.
        private readonly Stopwatch _fallback = new Stopwatch();

        private Stopwatch Watch(PageContext context) => context.Tablet != null ? context.Tablet.Session.Stopwatch : _fallback;

        public void Build(RectTransform parent, PageContext context, string pageId, int? rows)
        {
            var watch = Watch(context);
            watch.Resume(Time.time);
            var label = UIFactory.Label(parent, "", 48, Theme.Highlight, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(label, 70);
            LiveText.Attach(label, () => Text(watch.Elapsed(Time.time)));
        }

        public void Refresh(PageContext context) { }

        public IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            var watch = Watch(context);
            yield return new SoftKey(watch.Running ? "Stop" : "Start", () =>
            {
                if (watch.Running)
                    watch.Stop(Time.time);
                else
                    watch.Start(Time.time);
                context.Rebuild();
            });
            yield return new SoftKey("Reset", watch.Elapsed(Time.time) > 0f ? () =>
            {
                watch.Reset(Time.time);
                context.Rebuild();
            } : (Action)null);
        }

        private static string Text(float seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
        }
    }
}
