using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RatCapture
{
    /// <summary>Process loopback capture of a process tree, Windows 10 2004 and later. Yields 32-bit float interleaved stereo.</summary>
    public sealed class ProcessLoopback : IDisposable
    {
        public const int Channels = 2;

        private const string LoopbackDevice = "VAD\\Process_Loopback";
        private const int ActivationTypeProcessLoopback = 1;
        private const int IncludeTargetProcessTree = 0;
        private const ushort VtBlob = 65;
        private const uint StreamFlagsLoopback = 0x00020000;
        private const uint StreamFlagsEventCallback = 0x00040000;
        private const uint StreamFlagsAutoConvertPcm = 0x80000000;
        private const uint BufferFlagsDiscontinuity = 0x1;
        private const uint BufferFlagsSilent = 0x2;
        private const ushort WaveFormatIeeeFloat = 3;
        // 1 s in 100 ns units. The game reads stdout in chunks, so a shorter buffer would overflow.
        private const long BufferDuration = 10000000;

        private readonly IAudioClient _client;
        private readonly IAudioCaptureClient _capture;
        private readonly AutoResetEvent _ready = new AutoResetEvent(false);

        public int SampleRate { get; }

        /// <summary>Times Windows reported lost sound (buffer overflow) since the start.</summary>
        public int Discontinuities { get; private set; }

        /// <exception cref="COMException">Windows refused, e.g. before Windows 10 2004 or the process ended.</exception>
        public ProcessLoopback(int processId, int sampleRate)
        {
            SampleRate = sampleRate;
            _client = Activate(processId);

            var format = new WaveFormatEx
            {
                FormatTag = WaveFormatIeeeFloat,
                Channels = Channels,
                SamplesPerSec = sampleRate,
                BitsPerSample = 32,
                BlockAlign = Channels * 4,
                AvgBytesPerSec = sampleRate * Channels * 4,
            };
            IntPtr formatPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
            try
            {
                Marshal.StructureToPtr(format, formatPtr, false);
                Check(_client.Initialize(0, StreamFlagsLoopback | StreamFlagsEventCallback | StreamFlagsAutoConvertPcm,
                    BufferDuration, 0, formatPtr, IntPtr.Zero), "IAudioClient.Initialize");
            }
            finally
            {
                Marshal.FreeHGlobal(formatPtr);
            }

            Check(_client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle()), "IAudioClient.SetEventHandle");
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(_client.GetService(ref captureId, out object capture), "IAudioClient.GetService");
            _capture = (IAudioCaptureClient)capture;
        }

        public void Start() => Check(_client.Start(), "IAudioClient.Start");

        /// <summary>Waits up to <paramref name="timeoutMs"/> for sound. Returns the floats written, 0 if none came.</summary>
        public int Read(float[] samples, int timeoutMs)
        {
            _ready.WaitOne(timeoutMs);
            int written = 0;
            while (true)
            {
                Check(_capture.GetNextPacketSize(out uint packet), "GetNextPacketSize");
                if (packet == 0) return written;
                Check(_capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _), "GetBuffer");
                if ((flags & BufferFlagsDiscontinuity) != 0 && written + frames > 0)
                    Discontinuities++;
                int count = (int)Math.Min(frames * Channels, (uint)(samples.Length - written));
                if ((flags & BufferFlagsSilent) != 0)
                    Array.Clear(samples, written, count);
                else
                    Marshal.Copy(data, samples, written, count);
                written += count;
                Check(_capture.ReleaseBuffer(frames), "ReleaseBuffer");
                if (written == samples.Length) return written;
            }
        }

        public void Dispose()
        {
            _client.Stop();
            Marshal.ReleaseComObject(_capture);
            Marshal.ReleaseComObject(_client);
            _ready.Dispose();
        }

        private static IAudioClient Activate(int processId)
        {
            var parameters = new ActivationParams
            {
                ActivationType = ActivationTypeProcessLoopback,
                TargetProcessId = (uint)processId,
                ProcessLoopbackMode = IncludeTargetProcessTree,
            };
            IntPtr paramsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
            IntPtr variantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());
            try
            {
                Marshal.StructureToPtr(parameters, paramsPtr, false);
                Marshal.StructureToPtr(new BlobVariant
                {
                    Vt = VtBlob,
                    Size = (uint)Marshal.SizeOf<ActivationParams>(),
                    Data = paramsPtr,
                }, variantPtr, false);

                var handler = new CompletionHandler();
                ActivateAudioInterfaceAsync(LoopbackDevice, typeof(IAudioClient).GUID, variantPtr, handler, out _);
                handler.Done.WaitOne();
                Check(handler.Result, "ActivateAudioInterfaceAsync");
                return (IAudioClient)handler.Client;
            }
            finally
            {
                Marshal.FreeHGlobal(variantPtr);
                Marshal.FreeHGlobal(paramsPtr);
            }
        }

        private static void Check(int hr, string call)
        {
            if (hr < 0) throw new COMException($"{call} failed with 0x{hr:X8}", hr);
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation operation);

        // Windows calls this on its own thread (IAgileObject allows it). Public, as COM only finds interfaces of public types.
        public sealed class CompletionHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            public readonly ManualResetEvent Done = new ManualResetEvent(false);
            public int Result;
            public object Client;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
            {
                operation.GetActivateResult(out Result, out Client);
                Done.Set();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ActivationParams
        {
            public int ActivationType;
            public uint TargetProcessId;
            public int ProcessLoopbackMode;
        }

        // PROPVARIANT holding a BLOB, 64-bit layout.
        [StructLayout(LayoutKind.Sequential)]
        private struct BlobVariant
        {
            public ushort Vt;
            public ushort Reserved1;
            public ushort Reserved2;
            public ushort Reserved3;
            public uint Size;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormatEx
        {
            public ushort FormatTag;
            public ushort Channels;
            public int SamplesPerSec;
            public int AvgBytesPerSec;
            public ushort BlockAlign;
            public ushort BitsPerSample;
            public ushort ExtraSize;
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceCompletionHandler
        {
            void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IActivateAudioInterfaceAsyncOperation
        {
            void GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
        }

        [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAgileObject
        {
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
    }
}
