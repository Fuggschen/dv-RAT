using System;
using System.Collections.Generic;

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

        public void Home()
        {
            _history.Clear();
            _history.Add(_homeId);
            Navigated?.Invoke(_homeId);
            Changed?.Invoke(_homeId);
        }
    }
}
