using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DVRAT;

namespace RatCapture
{
    /// <summary>
    /// Turns session levels down to <see cref="Level"/> and the recording is boosted by its inverse. Windows remembers levels
    /// across restarts, so the old ones go to the restore file first, and a dead helper's get restored on the next start or by the failsafe.
    /// </summary>
    public sealed class Quieter
    {
        /// <summary>-80 dB: inaudible, while float keeps the sound intact.</summary>
        public const float Level = 0.0001f;
        /// <summary>The failsafe treats levels at or below this as left down by the helper.</summary>
        public const float LeftDownBelow = 0.001f;

        private readonly string _restoreFile;
        // By instance: the levels each stream had before.
        private readonly Dictionary<string, (AudioSession Session, float[] Before)> _changed =
            new Dictionary<string, (AudioSession, float[])>();

        public Quieter(string restoreFile) => _restoreFile = restoreFile;

        public bool On { get; private set; }

        /// <summary>Later sessions are handled by <see cref="Update"/>.</summary>
        public void TurnOn(ProcessTree tree, int root)
        {
            On = true;
            Update(tree, root);
        }

        /// <summary>Turns down sessions opened since, as the program may open new ones any time.</summary>
        public void Update(ProcessTree tree, int root)
        {
            if (!On) return;
            var fresh = AudioSessions.All()
                .Where(s => !_changed.ContainsKey(s.Instance) && tree.InTree(s.ProcessId, root))
                .ToList();
            if (fresh.Count == 0) return;
            foreach (var session in fresh)
            {
                // Levels left down by a dead run are not real levels to restore.
                var before = session.Levels;
                if (before.All(l => l <= LeftDownBelow))
                    before = before.Select(_ => 1f).ToArray();
                _changed[session.Instance] = (session, before);
            }
            // Written first, so a crash still leaves a way back.
            WriteRestoreFile();
            foreach (var session in fresh)
                session.SetAllLevels(Level);
        }

        public void TurnOff()
        {
            On = false;
            var failed = new List<string>();
            foreach (var pair in _changed)
                if (!pair.Value.Session.SetLevels(pair.Value.Before))
                    failed.Add(pair.Key);
            foreach (string instance in _changed.Keys.Except(failed).ToList())
                _changed.Remove(instance);
            // An ended stream keeps its entry, so the program's next sessions get levels back from the file.
            if (_changed.Count == 0)
                File.Delete(_restoreFile);
            else
                WriteRestoreFile();
            _changed.Clear();
        }

        private void WriteRestoreFile()
        {
            var lines = _changed.Values
                .Select(c => CaptureProtocol.LevelsLine(new SavedLevels(c.Session.Identifier, c.Before)))
                .Concat(ReadRestoreFile(_restoreFile).Select(CaptureProtocol.LevelsLine))
                .Distinct();
            File.WriteAllLines(_restoreFile, lines);
        }

        /// <summary>Entries without a live session stay in the file. Returns how many sessions were restored.</summary>
        public static int Restore(string restoreFile)
        {
            var saved = ReadRestoreFile(restoreFile);
            if (saved.Count == 0) return 0;
            var sessions = AudioSessions.All();
            int restored = 0;
            var left = new List<SavedLevels>();
            foreach (var entry in saved)
            {
                var matching = sessions.Where(s => s.Identifier == entry.SessionIdentifier).ToList();
                foreach (var session in matching)
                    if (session.SetLevels(entry.Levels)) restored++;
                if (matching.Count == 0) left.Add(entry);
            }
            if (left.Count == 0)
                File.Delete(restoreFile);
            else
                File.WriteAllLines(restoreFile, left.Select(CaptureProtocol.LevelsLine));
            return restored;
        }

        /// <summary>Works without a restore file.</summary>
        public static List<AudioSession> ResetLeftDown()
        {
            var reset = new List<AudioSession>();
            foreach (var session in AudioSessions.All())
            {
                var levels = session.Levels;
                if (levels.Length > 0 && levels.All(l => l <= LeftDownBelow) && session.SetAllLevels(1f))
                    reset.Add(session);
            }
            return reset;
        }

        private static List<SavedLevels> ReadRestoreFile(string path)
        {
            if (!File.Exists(path)) return new List<SavedLevels>();
            return File.ReadAllLines(path).Select(CaptureProtocol.ParseLevels).Where(l => l != null).ToList();
        }
    }
}
