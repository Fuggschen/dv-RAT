using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DVRAT;

namespace RatCapture
{
    /// <summary>
    /// ratcapture: failsafe, restores every program's volume (plain double-click)
    /// ratcapture list: programs playing sound now
    /// ratcapture run [process] [rate]: see <see cref="Runner"/>
    /// </summary>
    public static class Program
    {
        /// <summary>Next to the exe, so every run and the failsafe share it.</summary>
        private static string RestoreFile =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restore.txt");

        public static int Main(string[] args)
        {
            // Written from the recording and media threads.
            var status = TextWriter.Synchronized(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            try
            {
                switch (args.Length > 0 ? args[0] : "")
                {
                    case "":
                        Failsafe();
                        return 0;
                    case "list":
                        List();
                        return 0;
                    case "run" when args.Length == 3:
                        new Runner(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), RestoreFile, status).Run();
                        return 0;
                    default:
                        status.WriteLine("Use: ratcapture | ratcapture list | ratcapture run <process> <sample rate>");
                        return 2;
                }
            }
            catch (Exception ex)
            {
                status.WriteLine(CaptureProtocol.ErrorLine(ex.Message));
                if (args.Length == 0) WaitForKey();
                return 1;
            }
        }

        private static void Failsafe()
        {
            Console.WriteLine("R.A.T. tablet: putting every program's volume back to normal.");
            int restored = Quieter.Restore(RestoreFile);
            Console.WriteLine($"Put back the saved levels of {restored} sound session(s).");
            var tree = ProcessTree.Snapshot();
            var reset = Quieter.ResetLeftDown();
            foreach (var session in reset)
                Console.WriteLine($"Turned {tree.Name(session.ProcessId) ?? "an ended program"} back up to full volume.");
            if (restored == 0 && reset.Count == 0)
                Console.WriteLine("Nothing was turned down; all is as it should be.");
            WaitForKey();
        }

        private static void WaitForKey()
        {
            if (Console.IsInputRedirected) return;
            Console.WriteLine("Press any key to close.");
            Console.ReadKey(true);
        }

        private static void List()
        {
            var tree = ProcessTree.Snapshot();
            MediaControls media = null;
            try
            {
                media = new MediaControls();
            }
            catch (Exception)
            {
                // Without media controls, programs are listed without media.
            }

            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int self = Process.GetCurrentProcess().Id;
            foreach (var group in AudioSessions.All().GroupBy(s => tree.Root(s.ProcessId)))
            {
                string name = tree.Name(group.Key);
                // audiodg is Windows' audio engine, not a program.
                if (name == null || group.Key == self || name.Equals("audiodg", StringComparison.OrdinalIgnoreCase) || !seen.Add(name))
                    continue;
                string display = group.Select(s => s.DisplayName).FirstOrDefault(n => n.Length > 0 && !n.StartsWith("@"))
                    ?? Description(group.Key) ?? name;
                stdout.WriteLine(CaptureProtocol.AppLine(new CaptureApp(group.Key, name, display, media != null && media.HasSession(name))));
            }
            stdout.Flush();
        }

        /// <summary>The exe's file description; null when it cannot be read.</summary>
        private static string Description(int processId)
        {
            try
            {
                string description = Process.GetProcessById(processId).MainModule?.FileVersionInfo.FileDescription;
                return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
