using System;
using System.Collections.Generic;
using System.Linq;

namespace DVRAT
{
    /// <summary>Applies a list row's <c>filter</c>, <c>sort</c>, <c>descending</c> and <c>limit</c>. No Unity types, so it is unit testable.</summary>
    public static class ListQuery
    {
        public static List<object> Apply(IEnumerable<object> items, Dictionary<string, object> root, RowDefinition row,
            ICollection<string> missing = null)
        {
            var result = items.ToList();
            if (!string.IsNullOrWhiteSpace(row.Filter))
                result = result.Where(item => Template.Evaluate(row.Filter, PageBindings.WithItem(root, item), missing)).ToList();

            string sort = Template.Format(row.Sort, root, missing);
            if (!string.IsNullOrWhiteSpace(sort))
            {
                var keys = result.Select(item => SortKey(sort, PageBindings.WithItem(root, item), missing)).ToList();
                var order = Enumerable.Range(0, result.Count).ToList();
                // Numeric only when every key is a number, else all compare as text.
                bool numeric = keys.All(k => k.isNumber);
                Comparison<int> compare = numeric
                    ? (a, b) => keys[a].number.CompareTo(keys[b].number)
                    : (Comparison<int>)((a, b) => string.Compare(keys[a].text, keys[b].text, StringComparison.OrdinalIgnoreCase));
                // OrderBy is stable.
                var sorted = IsDescending(row.Descending, root, missing)
                    ? order.OrderByDescending(i => i, Comparer<int>.Create(compare))
                    : order.OrderBy(i => i, Comparer<int>.Create(compare));
                result = sorted.Select(i => result[i]).ToList();
            }

            if (row.Limit.HasValue && row.Limit.Value >= 0 && result.Count > row.Limit.Value)
                result = result.Take(row.Limit.Value).ToList();
            return result;
        }

        private static bool IsDescending(string descending, object root, ICollection<string> missing)
        {
            string text = Template.Format(descending, root, missing).Trim();
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Length == 0 || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return Template.Evaluate(text, root, missing);
        }

        private static (bool isNumber, double number, string text) SortKey(string sort, object root, ICollection<string> missing)
        {
            if (!Template.TryResolve(root, sort, out object value))
            {
                if (Expression.LooksLikeExpression(sort) && Expression.TryEvaluate(sort, root, out double computed, missing))
                    return (true, computed, Template.ToText(computed, null));
                if (!Expression.LooksLikeExpression(sort)) missing?.Add(sort.Trim());
                return (false, 0, "");
            }
            double number = 0;
            bool isNumber = !(value is string) && Expression.TryNumber(value, out number);
            return (isNumber, number, Template.ToText(value, null));
        }
    }
}
