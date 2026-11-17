using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DVRAT
{
    /// <summary>The programs PC Audio can play, listed on a worker thread and cached for <see cref="MaxAgeSeconds"/>.</summary>
    public static class CaptureApps
    {
        public const float MaxAgeSeconds = 3f;
        private const int ListTimeoutMs = 5000;

        private static readonly System.Diagnostics.Stopwatch Age = new System.Diagnostics.Stopwatch();
        private static volatile bool _asking;
        private static volatile List<CaptureApp> _latest;
        private static volatile string _error;

        /// <summary>Null until the first answer.</summary>
        public static List<CaptureApp> Latest => _latest;

        /// <summary>Null when the last ask worked.</summary>
        public static string Error => _error;

        /// <summary>Skipped while an ask is running or the list is fresh.</summary>
        public static void Request()
        {
            if (_asking || Age.IsRunning && Age.Elapsed.TotalSeconds < MaxAgeSeconds || !CaptureTrack.Available) return;
            _asking = true;
            string exe = CaptureTrack.ExePath;
            bool linux = UnderWine;
            Task.Run(() =>
            {
                try
                {
                    _latest = Sorted(linux ? AskLinux() : Ask(exe));
                    _error = null;
                }
                catch (Exception ex)
                {
                    _error = "The program list could not be read: " + ex.Message;
                    RATMain.LogWarning("PC Audio: " + _error);
                }
                Age.Restart();
                _asking = false;
            });
        }

        private static List<CaptureApp> Ask(string exe)
        {
            var start = new ProcessStartInfo(exe, "list")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            using (var process = Process.Start(start) ?? throw new InvalidOperationException("ratcapture did not start"))
            {
                var apps = new List<CaptureApp>();
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    var app = CaptureProtocol.ParseApp(line);
                    if (app != null) apps.Add(app);
                }
                if (!process.WaitForExit(ListTimeoutMs))
                    process.Kill();
                return apps;
            }
        }

        private static List<CaptureApp> AskLinux()
        {
            var apps = new List<CaptureApp>();
            var socket = LinuxBridge.Accept(LinuxBridge.Start("list"));
            LinuxBridge.ReadLines(socket, line =>
            {
                var app = CaptureProtocol.ParseApp(line);
                if (app != null) apps.Add(app);
                else LogHelperLine(line);
            });
            return apps;
        }

        // The game's own process; under Wine the Linux helper lists it as "DerailValley.exe".
        private static readonly string ThisGame = System.Diagnostics.Process.GetCurrentProcess().ProcessName;

        /// <summary>True for the game itself, which must not be captured: its sound would feed back, louder each time.</summary>
        public static bool IsThisGame(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4)
                : processName;
            return string.Equals(name, ThisGame, StringComparison.OrdinalIgnoreCase);
        }

        private static List<CaptureApp> Sorted(List<CaptureApp> apps)
        {
            apps.RemoveAll(app => IsThisGame(app.ProcessName) || IsThisGame(app.DisplayName));
            apps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase));
            return apps;
        }

        private static volatile bool _diagnosed;

        /// <summary>Under Wine, once per game: logs what the Linux helper sees.</summary>
        public static void LogLinuxDiagnosticsOnce()
        {
            if (_diagnosed || !UnderWine) return;
            _diagnosed = true;
            if (!LinuxBridge.Available)
            {
                RATMain.LogWarning("PC Audio (Linux): capture/ratcapture-linux is missing");
                return;
            }
            RATMain.Log("PC Audio (Linux): helper at " + LinuxBridge.UnixPath(LinuxBridge.HelperPath));
            Task.Run(() =>
            {
                try
                {
                    LinuxBridge.ReadLines(LinuxBridge.Accept(LinuxBridge.Start("diag")), LogHelperLine);
                }
                catch (Exception ex)
                {
                    RATMain.LogWarning("PC Audio (Linux): diagnostics failed: " + ex.Message);
                }
            });
        }

        private static void LogHelperLine(string line)
        {
            var e = CaptureProtocol.ParseEvent(line);
            if (e?.Kind == CaptureEventKind.Error) RATMain.LogWarning("PC Audio (Linux): " + e.Message);
            else RATMain.Log("PC Audio (Linux): " + (e?.Message ?? line));
        }

        /// <summary>True under Wine or Proton, which lack process loopback, so PC Audio uses <see cref="LinuxBridge"/>.</summary>
        public static bool UnderWine => WineCheck.Value;

        private static readonly Lazy<bool> WineCheck = new Lazy<bool>(() =>
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch (Exception)
            {
                return false;
            }
        });

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);
    }
}
