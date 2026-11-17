using System;

namespace DVRAT
{
    /// <summary>Logs steps slower than <see cref="ReportMs"/>, e.g. <c>using (Timing.Measure("Loading pages")) { ... }</c>.</summary>
    public struct Timing : IDisposable
    {
        /// <summary>About two frames at 60 fps.</summary>
        private const double ReportMs = 30;

        private readonly string _what;
        private readonly long _start;

        private Timing(string what)
        {
            _what = what;
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public static Timing Measure(string what) => new Timing(what);

        public void Dispose()
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms >= ReportMs)
                RATMain.Log($"Slow: {_what} took {ms:F0} ms");
        }
    }
}
