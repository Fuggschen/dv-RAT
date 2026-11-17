using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RatCapture
{
    /// <summary>Browsers and Spotify play from helper processes, so recording and quieting cover the whole tree.</summary>
    public sealed class ProcessTree
    {
        // Guards against a cycle through a reused parent id.
        private const int MaxDepth = 64;

        private readonly Dictionary<int, (int Parent, string Name)> _processes;

        private ProcessTree(Dictionary<int, (int, string)> processes) => _processes = processes;

        public static ProcessTree Snapshot()
        {
            var processes = new Dictionary<int, (int, string)>();
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapProcesses, 0);
            if (snapshot == InvalidHandle)
                throw new InvalidOperationException("The process list could not be read: " + Marshal.GetLastWin32Error());
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                for (bool more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                    processes[(int)entry.ProcessId] = ((int)entry.ParentProcessId, Path.GetFileNameWithoutExtension(entry.ExeFile));
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return new ProcessTree(processes);
        }

        /// <summary>Exe name without ".exe"; null for an ended process.</summary>
        public string Name(int processId) => _processes.TryGetValue(processId, out var process) ? process.Name : null;

        /// <summary>Goes up through parents with the same exe name.</summary>
        public int Root(int processId)
        {
            int current = processId;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (!_processes.TryGetValue(current, out var process) || !_processes.TryGetValue(process.Parent, out var parent) ||
                    process.Parent == current || !string.Equals(parent.Name, process.Name, StringComparison.OrdinalIgnoreCase))
                    return current;
                current = process.Parent;
            }
            return current;
        }

        /// <summary>0 while no process named <paramref name="name"/> runs.</summary>
        public int RootOf(string name) =>
            _processes.Where(p => string.Equals(p.Value.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(p => Root(p.Key)).DefaultIfEmpty(0).Min();

        public bool InTree(int processId, int root)
        {
            int current = processId;
            for (int depth = 0; depth < MaxDepth; depth++)
            {
                if (current == root) return true;
                if (!_processes.TryGetValue(current, out var process) || process.Parent == current) return false;
                current = process.Parent;
            }
            return false;
        }

        private const uint SnapProcesses = 0x2;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size;
            public uint Usage;
            public uint ProcessId;
            public IntPtr DefaultHeapId;
            public uint ModuleId;
            public uint Threads;
            public uint ParentProcessId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
