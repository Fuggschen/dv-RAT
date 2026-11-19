using System;
using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>One screen of the tablet, built from code into <see cref="PageContext.Content"/>.</summary>
    public abstract class Page
    {
        protected Page(string id, string title, string parentId = PageIds.Home, int order = 100)
        {
            Id = id;
            Title = title;
            ParentId = parentId;
            Order = order;
        }

        public string Id { get; }
        public string Title { get; }

        /// <summary>Menu page that lists this page. Null when reachable only by link.</summary>
        public string ParentId { get; }

        /// <summary>Sort key in the parent menu. Ties sort by title.</summary>
        public int Order { get; }

        /// <summary>The page's own Start menu entry, above its subpages.</summary>
        public string OpenLabel { get; protected set; }

        /// <summary>True when opening the page waits for the radio signal.</summary>
        public bool NeedsSignal { get; protected set; }

        /// <summary>True when the page shows fees. The data refresh reads fees only for such a page.</summary>
        public virtual bool UsesFees => false;

        /// <summary>False when the parent page shows no soft key for this page.</summary>
        public bool ParentKey { get; protected set; } = true;

        /// <summary>False when the Start menu does not list this page.</summary>
        public bool InStartMenu { get; protected set; } = true;

        /// <summary>Page variables and the values they go back to when the tablet leaves this page and its subpages.</summary>
        public IReadOnlyDictionary<string, string> LeaveVars { get; protected set; } = new Dictionary<string, string>();

        /// <summary>True for a page that only lists subpages. The Start menu shows them without the page.</summary>
        public virtual bool IsMenu => false;

        /// <summary>Called each time the page is shown, with an empty content area.</summary>
        public abstract void Build(PageContext context);

        /// <summary>Called after every data refresh while the page is shown.</summary>
        public virtual void Refresh(PageContext context) { }

        /// <summary>Page soft keys, shown after the fixed navigation keys.</summary>
        public virtual IEnumerable<SoftKey> SoftKeys(PageContext context)
        {
            yield break;
        }

        /// <summary>True while an inner view is open that Back leaves before the page.</summary>
        public virtual bool CanGoBack => false;

        /// <summary>Leaves the inner view.</summary>
        public virtual void GoBack(PageContext context) { }
    }

    public readonly struct SoftKey
    {
        public SoftKey(string label, Action action, bool half = false, bool right = false)
        {
            Label = label;
            Action = action;
            Half = half;
            Right = right;
        }

        public string Label { get; }
        public Action Action { get; }

        /// <summary>Half width. Two half keys in a row share one slot.</summary>
        public bool Half { get; }

        /// <summary>Sits at the right end of the bar.</summary>
        public bool Right { get; }

        public SoftKey WithAction(Action action) => new SoftKey(Label, action, Half, Right);

        /// <summary>An empty slot that pushes keys to the right.</summary>
        public static SoftKey Blank => new SoftKey(null, null);
    }

    public class PageContext
    {
        public PageContext(RectTransform content, Navigator navigator, PageRegistry registry,
            RATTabletController tablet, Action rebuild, Action refresh)
        {
            Content = content;
            Navigator = navigator;
            Registry = registry;
            Tablet = tablet;
            _rebuild = rebuild;
            _refresh = refresh;
        }

        private readonly Action _rebuild;
        private readonly Action _refresh;

        /// <summary>Stretched column to build into.</summary>
        public RectTransform Content { get; }
        public Navigator Navigator { get; }
        public PageRegistry Registry { get; }
        public RATTabletController Tablet { get; }

        public TrainData Train => Tablet != null ? Tablet.CurrentTrainData : null;
        public RouteData Route => Tablet != null ? Tablet.CurrentRouteData : null;
        public JobData Jobs => Tablet != null ? Tablet.CurrentJobData : null;
        public WorldData World => Tablet != null ? Tablet.CurrentWorldData : null;

        /// <summary>The page's view state, see <see cref="TabletSession.ViewVars"/>. Null without a tablet.</summary>
        public Dictionary<string, string> View(string pageId) => Tablet != null ? Tablet.Session.View(pageId) : null;

        /// <summary>Clears the content area and builds the page again.</summary>
        public void Rebuild() => _rebuild();

        /// <summary>Updates values and soft keys in place, like the data refresh.</summary>
        public void Refresh() => _refresh();
    }

    public static class PageIds
    {
        public const string Home = "home";
    }
}
