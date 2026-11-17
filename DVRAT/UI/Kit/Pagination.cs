using System.Collections.Generic;

namespace DVRAT
{
    /// <summary>Splits rows into screens without breaking up row groups. Holds no Unity types so it can be unit tested.</summary>
    public static class Pagination
    {
        /// <summary>First row index of every screen. Only a group longer than a screen is split.</summary>
        public static List<int> ScreenStarts(IReadOnlyList<int> groupSizes, int rowsPerScreen)
        {
            var starts = new List<int> { 0 };
            int used = 0;
            int row = 0;
            foreach (int size in groupSizes)
            {
                if (used > 0 && used + size > rowsPerScreen)
                {
                    starts.Add(row);
                    used = 0;
                }
                for (int i = 0; i < size; i++)
                {
                    if (used == rowsPerScreen)
                    {
                        starts.Add(row);
                        used = 0;
                    }
                    used++;
                    row++;
                }
            }
            return starts;
        }
    }
}
