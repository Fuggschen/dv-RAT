using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DVRAT
{
    /// <summary>Fills <c>{path:format}</c> placeholders and evaluates <c>when</c> conditions. No Unity types, so it is unit testable.</summary>
    public static class Template
    {
        /// <summary>Lists answer <c>count</c>. So does text, counting comma-separated values, since page variables store lists that way.</summary>
        public static bool TryResolve(object root, string path, out object value)
        {
            value = root;
            foreach (string part in path.Trim().Split('.'))
            {
                if (value is IDictionary<string, object> map)
                {
                    if (!map.TryGetValue(part, out value)) return false;
                }
                else if (value is ICollection collection && part == "count")
                {
                    value = collection.Count;
                }
                else if (value is string text && part == "count")
                {
                    value = Values(text).Count;
                }
                else
                {
                    value = null;
                    return false;
                }
            }
            return true;
        }

        /// <summary>Unknown paths show as <c>{?path}</c> and go to <paramref name="missing"/>.</summary>
        public static string Format(string template, object root, ICollection<string> missing = null)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? "";

            var result = new StringBuilder(template.Length);
            int position = 0;
            while (position < template.Length)
            {
                int open = template.IndexOf('{', position);
                int close = open >= 0 ? template.IndexOf('}', open + 1) : -1;
                if (open < 0 || close < 0)
                {
                    result.Append(template, position, template.Length - position);
                    break;
                }

                result.Append(template, position, open - position);
                string inner = template.Substring(open + 1, close - open - 1);
                int colon = inner.IndexOf(':');
                string path = colon >= 0 ? inner.Substring(0, colon) : inner;
                string format = colon >= 0 ? inner.Substring(colon + 1) : null;

                if (TryResolve(root, path, out object value))
                {
                    result.Append(ToText(value, format));
                }
                else if (Expression.LooksLikeExpression(path))
                {
                    if (Expression.TryEvaluate(path, root, out double number, missing))
                        result.Append(double.IsNaN(number) || double.IsInfinity(number) ? "--" : ToText(number, format));
                    else
                        result.Append("{?").Append(path.Trim()).Append('}');
                }
                else
                {
                    missing?.Add(path.Trim());
                    result.Append("{?").Append(path.Trim()).Append('}');
                }
                position = close + 1;
            }
            return result.ToString();
        }

        /// <summary>
        /// Forms: empty (true), <c>path</c>, <c>!path</c>, <c>path=value</c>, <c>path!=value</c>, <c>in</c>, <c>!in</c>
        /// and number comparisons. Join with <c>&amp;&amp;</c> and <c>||</c>, where <c>&amp;&amp;</c> binds tighter.
        /// </summary>
        public static bool Evaluate(string condition, object root, ICollection<string> missing = null)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;

            bool any = false;
            // No short-circuit, so every unknown path is reported.
            foreach (string alternative in condition.Split(new[] { "||" }, StringSplitOptions.None))
            {
                bool all = true;
                foreach (string part in alternative.Split(new[] { "&&" }, StringSplitOptions.None))
                    all &= EvaluateSingle(part.Trim(), root, missing);
                any |= all;
            }
            return any;
        }

        // The spaces keep " in " from matching inside paths like "item.index".
        private static readonly string[] Comparisons = { " !in ", " in ", "!=", "<=", ">=", "=", "<", ">" };

        private static bool EvaluateSingle(string condition, object root, ICollection<string> missing)
        {
            if (condition.Length == 0) return true;

            // First operator wins, the value side may contain any characters.
            int split = -1;
            string op = null;
            for (int i = 0; i < condition.Length && op == null; i++)
            {
                foreach (string candidate in Comparisons)
                {
                    if (string.CompareOrdinal(condition, i, candidate, 0, candidate.Length) != 0) continue;
                    split = i;
                    op = candidate;
                    break;
                }
            }

            if (op == " in " || op == " !in ")
                return Intersects(condition.Substring(0, split).Trim(), condition.Substring(split + op.Length).Trim(), root, missing)
                    != (op == " !in ");

            if (op != null)
            {
                string left = condition.Substring(0, split).Trim();
                string right = Format(condition.Substring(split + op.Length).Trim(), root, missing);
                if (op == "=" || op == "!=")
                {
                    string actual = TryResolve(root, left, out object value) ? ToText(value, null)
                        : Expression.LooksLikeExpression(left) && Expression.TryEvaluate(left, root, out double number, missing) ? ToText(number, null)
                        : Missing(left, missing);
                    return (actual == right) != (op == "!=");
                }

                bool known = TryNumber(left, root, missing, out double a) & TryNumber(right, root, missing, out double b);
                if (!known) return false;
                switch (op)
                {
                    case "<": return a < b;
                    case "<=": return a <= b;
                    case ">": return a > b;
                    default: return a >= b;
                }
            }

            bool negate = condition.StartsWith("!", StringComparison.Ordinal);
            string target = negate ? condition.Substring(1).Trim() : condition;
            bool truthy = false;
            if (TryResolve(root, target, out object resolved))
                truthy = IsTruthy(resolved);
            else
                missing?.Add(target);
            return truthy != negate;
        }

        /// <summary>False for null, false, empty text, zero and empty lists.</summary>
        public static bool IsTruthy(object value)
        {
            switch (value)
            {
                case null: return false;
                case bool b: return b;
                case string s: return s.Length > 0;
                case ICollection c: return c.Count > 0;
                case IConvertible number when IsNumber(number): return number.ToDouble(CultureInfo.InvariantCulture) != 0;
                default: return true;
            }
        }

        public static string ToText(object value, string format)
        {
            if (value == null) return "";
            if (value is bool b) return b ? "true" : "false";
            if (value is IFormattable formattable)
                return formattable.ToString(string.IsNullOrEmpty(format) ? null : format, CultureInfo.InvariantCulture);
            return value.ToString();
        }

        /// <summary>Either side may be a list or comma-separated text. A right side that is no path is read as text.</summary>
        private static bool Intersects(string left, string right, object root, ICollection<string> missing)
        {
            if (!TryResolve(root, left, out object leftValue))
            {
                missing?.Add(left);
                return false;
            }

            ICollection<string> set;
            if (TryResolve(root, right, out object rightValue))
                set = Values(rightValue);
            else
            {
                if (right.Contains(".")) missing?.Add(right);
                set = Values(right);
            }
            return Values(leftValue).Any(set.Contains);
        }

        public static HashSet<string> Values(object value)
        {
            var values = value is ICollection collection && !(value is string)
                ? collection.Cast<object>().Select(item => ToText(item, null))
                : ToText(value, null).Split(',');
            return new HashSet<string>(values.Select(v => v.Trim()).Where(v => v.Length > 0));
        }

        /// <summary>False when the path is unknown, not a number, or the arithmetic divides by zero.</summary>
        public static bool TryNumber(string text, object root, ICollection<string> missing, out double number)
        {
            if (TryResolve(root, text, out object value))
                return Expression.TryNumber(value, out number);
            return Expression.TryEvaluate(text, root, out number, missing) && !double.IsNaN(number);
        }

        private static string Missing(string path, ICollection<string> missing)
        {
            missing?.Add(path);
            return null;
        }

        private static bool IsNumber(IConvertible value)
        {
            switch (value.GetTypeCode())
            {
                case TypeCode.Byte: case TypeCode.SByte: case TypeCode.Int16: case TypeCode.UInt16:
                case TypeCode.Int32: case TypeCode.UInt32: case TypeCode.Int64: case TypeCode.UInt64:
                case TypeCode.Single: case TypeCode.Double: case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }
    }
}
