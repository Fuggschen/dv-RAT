using System;
using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Soft key bar slots keys take: two half keys in a row share one. Holds no Unity types so it can be unit tested.</summary>
    public static class SoftKeySlots
    {
        public static int Count<T>(IEnumerable<T> keys, Func<T, bool> isHalf)
        {
            int used = 0;
            bool open = false;
            foreach (var key in keys)
            {
                bool half = isHalf(key);
                if (half && open)
                {
                    open = false;
                    continue;
                }
                used++;
                open = half;
            }
            return used;
        }

        public static int Fitting<T>(IReadOnlyList<T> keys, Func<T, bool> isHalf, int slots)
        {
            int used = 0;
            bool open = false;
            for (int i = 0; i < keys.Count; i++)
            {
                bool half = isHalf(keys[i]);
                if (half && open)
                {
                    open = false;
                    continue;
                }
                if (used == slots) return i;
                used++;
                open = half;
            }
            return keys.Count;
        }
    }
}
