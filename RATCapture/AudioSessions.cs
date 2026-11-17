using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RatCapture
{
    public sealed class AudioSession
    {
        internal AudioSession(AudioSessions.IChannelAudioVolume volume, int processId, string identifier, string instance, string displayName)
        {
            Volume = volume;
            ProcessId = processId;
            Identifier = identifier;
            Instance = instance;
            DisplayName = displayName;
        }

        internal AudioSessions.IChannelAudioVolume Volume { get; }
        public int ProcessId { get; }
        /// <summary>Stable across restarts for a program on an output.</summary>
        public string Identifier { get; }
        /// <summary>Unique to this stream while it lasts.</summary>
        public string Instance { get; }
        /// <summary>Often empty.</summary>
        public string DisplayName { get; }

        /// <summary>Channel levels, 0 to 1. They multiply with the mixer slider.</summary>
        public float[] Levels
        {
            get
            {
                AudioSessions.Check(Volume.GetChannelCount(out uint count), "GetChannelCount");
                var levels = new float[count];
                AudioSessions.Check(Volume.GetAllVolumes(count, levels), "GetAllVolumes");
                return levels;
            }
        }

        /// <summary>False when Windows refused, e.g. the stream ended.</summary>
        public bool SetLevels(float[] levels)
        {
            var context = AudioSessions.EventContext;
            return Volume.SetAllVolumes((uint)levels.Length, levels, ref context) >= 0;
        }

        public bool SetAllLevels(float level)
        {
            int channels = Levels.Length;
            var levels = new float[channels];
            for (int c = 0; c < channels; c++) levels[c] = level;
            return SetLevels(levels);
        }
    }

    /// <summary>Quieting uses channel levels, as Windows refuses other processes the slider (ISimpleAudioVolume).</summary>
    public static class AudioSessions
    {
        internal static readonly Guid EventContext = Guid.NewGuid();

        private const int RenderFlow = 0;
        private const int ActiveDevices = 1;
        private const int ClsctxAll = 0x17;

        public static List<AudioSession> All()
        {
            var all = new List<AudioSession>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Check(enumerator.EnumAudioEndpoints(RenderFlow, ActiveDevices, out IMMDeviceCollection devices), "EnumAudioEndpoints");
            Check(devices.GetCount(out int deviceCount), "IMMDeviceCollection.GetCount");
            for (int d = 0; d < deviceCount; d++)
            {
                Check(devices.Item(d, out IMMDevice device), "IMMDeviceCollection.Item");
                var managerId = typeof(IAudioSessionManager2).GUID;
                Check(device.Activate(ref managerId, ClsctxAll, IntPtr.Zero, out object managerObject), "IMMDevice.Activate");
                var manager = (IAudioSessionManager2)managerObject;
                Check(manager.GetSessionEnumerator(out IAudioSessionEnumerator sessions), "GetSessionEnumerator");
                Check(sessions.GetCount(out int count), "GetCount");
                for (int i = 0; i < count; i++)
                {
                    Check(sessions.GetSession(i, out IAudioSessionControl2 session), "GetSession");
                    if (session.IsSystemSoundsSession() == 0) continue;
                    if (session.GetProcessId(out uint processId) < 0 || processId == 0) continue;
                    if (!(session is IChannelAudioVolume volume)) continue;
                    all.Add(new AudioSession(volume, (int)processId,
                        Text(session.GetSessionIdentifier(out IntPtr identifier), identifier),
                        Text(session.GetSessionInstanceIdentifier(out IntPtr instance), instance),
                        Text(session.GetDisplayName(out IntPtr name), name)));
                }
            }
            return all;
        }

        private static string Text(int hr, IntPtr text)
        {
            if (hr < 0 || text == IntPtr.Zero) return "";
            string value = Marshal.PtrToStringUni(text);
            Marshal.FreeCoTaskMem(text);
            return value ?? "";
        }

        internal static void Check(int hr, string call)
        {
            if (hr < 0) throw new COMException($"{call} failed with 0x{hr:X8}", hr);
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid grouping);
            [PreserveSig] int SetGroupingParam(IntPtr grouping, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint processId);
            // S_OK (0) for the system sounds session, S_FALSE (1) otherwise.
            [PreserveSig] int IsSystemSoundsSession();
        }

        [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IChannelAudioVolume
        {
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid context);
            [PreserveSig] int GetChannelVolume(uint index, out float level);
            [PreserveSig] int SetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray)] float[] levels, ref Guid context);
            [PreserveSig] int GetAllVolumes(uint count, [MarshalAs(UnmanagedType.LPArray), Out] float[] levels);
        }
    }
}
