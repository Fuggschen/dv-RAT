using System;
using System.Collections.Generic;
using System.Globalization;

namespace DVRAT
{
    /// <summary>Arithmetic in placeholders and conditions, e.g. <c>train.lengthM / 18</c>. No Unity types, so it is unit testable.</summary>
    public static class Expression
    {
        public static bool LooksLikeExpression(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return false;
            if (char.IsDigit(text[0])) return true;
            foreach (char c in text)
                if ("+-*/%()".IndexOf(c) >= 0) return true;
            return false;
        }

        /// <summary>Unknown paths go to <paramref name="missing"/>. Division by zero gives NaN.</summary>
        public static bool TryEvaluate(string text, object root, out double value, ICollection<string> missing = null)
        {
            var parser = new Parser(text, root, missing);
            try
            {
                value = parser.Sum();
                parser.SkipSpaces();
                if (!parser.AtEnd) throw new FormatException();
                return !parser.Failed;
            }
            catch (FormatException)
            {
                value = double.NaN;
                return false;
            }
        }

        /// <summary>Booleans count as 1/0.</summary>
        public static bool TryNumber(object value, out double number)
        {
            switch (value)
            {
                case bool b:
                    number = b ? 1 : 0;
                    return true;
                case string s:
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                case float f:
                    // Via shortest text, so 42.6f equals 42.6 and not 42.5999985.
                    number = double.Parse(f.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
                    return true;
                case IConvertible convertible when !(value is char) && !(value is DateTime):
                    try
                    {
                        number = convertible.ToDouble(CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch (Exception ex) when (ex is FormatException || ex is InvalidCastException)
                    {
                        break;
                    }
            }
            number = 0;
            return false;
        }

        private class Parser
        {
            private readonly string _text;
            private readonly object _root;
            private readonly ICollection<string> _missing;
            private int _position;

            public Parser(string text, object root, ICollection<string> missing)
            {
                _text = text ?? "";
                _root = root;
                _missing = missing;
            }

            /// <summary>Parsing continues after a failure so every bad path is reported.</summary>
            public bool Failed { get; private set; }

            public bool AtEnd => _position >= _text.Length;

            public double Sum()
            {
                double value = Product();
                while (true)
                {
                    if (Take('+')) value += Product();
                    else if (Take('-')) value -= Product();
                    else return value;
                }
            }

            private double Product()
            {
                double value = Unary();
                while (true)
                {
                    if (Take('*')) value *= Unary();
                    else if (Take('/')) value = Divide(value, Unary());
                    else if (Take('%')) value %= Unary();
                    else return value;
                }
            }

            private static double Divide(double a, double b) => b == 0 ? double.NaN : a / b;

            private double Unary()
            {
                if (Take('-')) return -Unary();
                if (Take('+')) return Unary();
                if (Take('('))
                {
                    double value = Sum();
                    if (!Take(')')) throw new FormatException();
                    return value;
                }

                SkipSpaces();
                int start = _position;
                if (!AtEnd && (char.IsDigit(_text[_position]) || _text[_position] == '.'))
                {
                    while (!AtEnd && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
                    return double.Parse(_text.Substring(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                while (!AtEnd && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_' || _text[_position] == '.')) _position++;
                if (_position == start) throw new FormatException();

                string path = _text.Substring(start, _position - start);
                if (Template.TryResolve(_root, path, out object resolved) && TryNumber(resolved, out double number))
                    return number;
                if (!Template.TryResolve(_root, path, out _)) _missing?.Add(path);
                Failed = true;
                return 0;
            }

            private bool Take(char c)
            {
                SkipSpaces();
                if (AtEnd || _text[_position] != c) return false;
                _position++;
                return true;
            }

            public void SkipSpaces()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_position])) _position++;
            }
        }
    }
}
