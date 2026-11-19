using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Page history by page ID. Holds no Unity state, so it is unit testable.</summary>
    public class Navigator
    {
        private const int MaxHistory = 32;

        private readonly List<string> _history = new List<string>();
        private readonly string _homeId;

        public Navigator(string homeId)
        {
            _homeId = homeId;
        }

        public string Current => _history.Count > 0 ? _history[_history.Count - 1] : _homeId;

        /// <summary>Page IDs, oldest first. The last is <see cref="Current"/>.</summary>
        public IReadOnlyList<string> History => _history;
        public bool CanGoBack => _history.Count > 1;

        public event Action<string> Changed;

        /// <summary>Raised before <see cref="Changed"/> when the player goes to a page, not by <see cref="Restore"/>.</summary>
        public event Action<string> Navigated;

        public void Open(string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || pageId == Current && _history.Count > 0) return;

            _history.Add(pageId);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
            Navigated?.Invoke(pageId);
            Changed?.Invoke(pageId);
        }

        /// <summary>Opens a page without adding a history entry.</summary>
        public void Replace(string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || pageId == Current) return;

            if (_history.Count > 0)
                _history[_history.Count - 1] = pageId;
            else
                _history.Add(pageId);
            Navigated?.Invoke(pageId);
            Changed?.Invoke(pageId);
        }

        public void Back()
        {
            if (!CanGoBack) return;

            _history.RemoveAt(_history.Count - 1);
            Navigated?.Invoke(Current);
            Changed?.Invoke(Current);
        }

        /// <summary>Replaces the history and opens its last page, or Home when empty.</summary>
        public void Restore(IEnumerable<string> history)
        {
            _history.Clear();
            foreach (string pageId in history ?? new string[0])
                if (!string.IsNullOrEmpty(pageId) && (_history.Count == 0 || _history[_history.Count - 1] != pageId))
                    _history.Add(pageId);
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, _history.Count - MaxHistory);
            if (_history.Count == 0)
                _history.Add(_homeId);
            Changed?.Invoke(Current);
        }

        /// <summary>
        /// True when going to <paramref name="targetId"/> leaves <paramref name="pageId"/> and its subpages: the target is outside them and is
        /// one of the page's parents, or the page is no longer in <paramref name="history"/>. Links out of the subpages, such as a car type page,
        /// do not leave, as Back returns from them.
        /// </summary>
        public static bool Leaves(string targetId, string pageId, IReadOnlyList<string> history, Func<string, string> parentOf) =>
            !IsWithin(targetId, pageId, parentOf) && (IsWithin(pageId, targetId, parentOf) || !history.Contains(pageId));

        /// <summary>True when <paramref name="id"/> is <paramref name="rootId"/> or one of its subpages at any depth.</summary>
        public static bool IsWithin(string id, string rootId, Func<string, string> parentOf)
        {
            var seen = new HashSet<string>();
            for (string current = id; current != null && seen.Add(current); current = parentOf(current))
                if (current == rootId)
                    return true;
            return false;
        }

        public void Home()
        {
            _history.Clear();
            _history.Add(_homeId);
            Navigated?.Invoke(_homeId);
            Changed?.Invoke(_homeId);
        }
    }
}
