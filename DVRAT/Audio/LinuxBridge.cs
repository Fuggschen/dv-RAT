using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DVRAT
{
    /// <summary>
    /// Under Proton or Wine: runs capture/ratcapture-linux, PC Audio's Linux helper. Wine gives no pipes or handle, so
    /// the helper connects back over loopback TCP. Frame: type byte ('A' sound, 'S' line), 4-byte little-endian length, payload.
    /// </summary>
    public static class LinuxBridge
    {
        private const int ConnectTimeoutMs = 5000;

        public static string HelperPath =>
            RATMain.ModEntry != null ? Path.Combine(RATMain.ModEntry.Path, "capture", "ratcapture-linux") : null;

        public static bool Available => HelperPath != null && File.Exists(HelperPath);

        /// <summary>Starts the helper with the arguments plus the port it should connect back to.</summary>
        public static TcpListener Start(params string[] arguments)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Launch(arguments.Concat(new[] { port.ToString() }).ToArray());
            }
            catch
            {
                listener.Stop();
                throw;
            }
            return listener;
        }

        public static Socket Accept(TcpListener listener)
        {
            try
            {
                if (!listener.Server.Poll(ConnectTimeoutMs * 1000, SelectMode.SelectRead))
                    throw new IOException("the Linux helper did not start; see the log for PC Audio (Linux) diagnostics");
                return listener.AcceptSocket();
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>Sound frames are skipped.</summary>
        public static void ReadLines(Socket socket, Action<string> onLine)
        {
            using (var stream = new NetworkStream(socket, true))
            using (var frames = new FrameReader(stream, onLine))
            {
                var skip = new byte[16384];
                while (frames.Read(skip, 0, skip.Length) > 0)
                {
                }
            }
        }

        private static void Launch(string[] arguments)
        {
            string helper = Quote(UnixPath(HelperPath));
            string script = $"chmod +x {helper} 2>/dev/null; exec {helper} {string.Join(" ", arguments.Select(Quote))}";
            // Wine splits this the Windows way. Backslashes stay literal unless a quote follows.
            string commandLine = "Z:\\bin\\sh -c \"" + script.Replace("\"", "\\\"") + "\"";
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow,
                    IntPtr.Zero, null, ref startup, out var info))
                throw new IOException("Wine could not start /bin/sh: error " + Marshal.GetLastWin32Error());
            // Wine returns no real handles for a Linux program.
            if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
            if (info.hThread != IntPtr.Zero) CloseHandle(info.hThread);
        }

        /// <summary>Single-quotes for sh.</summary>
        private static string Quote(string text) => "'" + text.Replace("'", "'\\''") + "'";

        /// <summary>Uses wine_get_unix_file_name, since the game may sit on any mapped drive. Falls back to Z: (maps to /).</summary>
        public static string UnixPath(string windowsPath)
        {
            try
            {
                IntPtr kernel32 = GetModuleHandle("kernel32.dll");
                IntPtr convert = kernel32 != IntPtr.Zero ? GetProcAddress(kernel32, "wine_get_unix_file_name") : IntPtr.Zero;
                if (convert != IntPtr.Zero)
                {
                    var toUnix = Marshal.GetDelegateForFunctionPointer<WineGetUnixFileName>(convert);
                    IntPtr result = toUnix(windowsPath);
                    if (result != IntPtr.Zero)
                    {
                        int length = 0;
                        while (Marshal.ReadByte(result, length) != 0) length++;
                        var bytes = new byte[length];
                        Marshal.Copy(result, bytes, 0, length);
                        HeapFree(GetProcessHeap(), 0, result);
                        return Encoding.UTF8.GetString(bytes);
                    }
                }
            }
            catch (Exception ex)
            {
                RATMain.LogWarning("PC Audio (Linux): wine_get_unix_file_name failed: " + ex.Message);
            }
            string full = Path.GetFullPath(windowsPath);
            return full.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)
                ? full.Substring(2).Replace('\\', '/')
                : full.Replace('\\', '/');
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate IntPtr WineGetUnixFileName(string dosPath);

        private const uint CreateNoWindow = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(string applicationName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcessHeap();

        [DllImport("kernel32.dll")]
        private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr memory);
    }
}
