using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace MusicStructure;

record Source(int Pid, [property: System.Text.Json.Serialization.JsonConverter(typeof(TickString))] long Started, string Label);

[SupportedOSPlatform("windows10.0.22000")]
static class Wasapi
{
    public static Source[] Sources()
    {
        using var current = Process.GetCurrentProcess();
        int session = current.SessionId;
        var spotify = new Dictionary<int, long>();
        foreach (var process in Process.GetProcessesByName("Spotify"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == session) spotify[process.Id] = process.StartTime.ToUniversalTime().Ticks;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
        if (spotify.Count == 0) return [];

        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (!Process32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var roots = new List<Source>();
        do
        {
            if (!spotify.TryGetValue(entry.Pid, out long started)) continue;
            if (spotify.TryGetValue(entry.Parent, out long parentStarted) && parentStarted <= started) continue;
            roots.Add(new Source(entry.Pid, started, $"Spotify desktop (PID {entry.Pid})"));
        } while (Process32Next(snapshot, ref entry));
        return roots.OrderBy(s => s.Started).ToArray();
    }

    public static void Capture(Source source, Action<Packet> enqueue, CancellationToken stop)
    {
        using var target = Process.GetProcessById(source.Pid);
        // Retain the selected process object without requesting write access.
        using var identity = OpenProcess(0x100000, false, source.Pid); // SYNCHRONIZE
        if (identity.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        VerifyTarget(target, identity, source);
        Check(CoInitializeEx(IntPtr.Zero, 0)); // MTA
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        using var ready = new AutoResetEvent(false);
        bool started = false;
        try
        {
            client = Activate(source.Pid, stop);
            VerifyTarget(target, identity, source);
            var format = new WaveFormat
            {
                Tag = 1, Channels = Packet.Channels, Rate = Packet.Rate, Bits = 16,
                BlockAlign = Packet.Channels * 2, AverageBytes = Packet.Rate * Packet.Channels * 2
            };
            const uint Loopback = 0x20000, EventCallback = 0x40000, AutoConvertPcm = 0x80000000;
            Check(client.Initialize(0, Loopback | EventCallback | AutoConvertPcm, 0, 0, ref format, IntPtr.Zero));
            Guid iid = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref iid, out object service));
            capture = (IAudioCaptureClient)service;
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            stop.ThrowIfCancellationRequested();
            Check(client.Start());
            started = true;

            WaitHandle[] events = [stop.WaitHandle, ready];
            long sequence = 0;
            double lastPacket = Clock.Milliseconds;
            double nextHealthCheck = lastPacket;
            double? expectedEnd = null;
            while (!stop.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(events, 250) == 0) break;
                double now = Clock.Milliseconds;
                if (now >= nextHealthCheck)
                {
                    VerifyTarget(target, identity, source);
                    nextHealthCheck = now + 500;
                }
                Check(capture.GetNextPacketSize(out uint available));
                if (available == 0 && now - lastPacket > 750)
                {
                    // Allow a normal packet delivery lead when closing the gap on resumption.
                    enqueue(new Packet(sequence, now - 60, [], Interruption: "Capture packets stopped arriving"));
                    expectedEnd = null;
                }
                while (available > 0 && !stop.IsCancellationRequested)
                {
                    Check(capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out ulong qpc));
                    Packet packet;
                    try
                    {
                        double start = qpc / 10_000.0; // WASAPI returns QPC in 100 ns units.
                        const uint TimestampError = 4, Silent = 2;
                        if ((flags & TimestampError) != 0 || start <= 0 || start > now + 100)
                        {
                            flags |= TimestampError;
                            start = expectedEnd ?? (Clock.Milliseconds - 1000.0 * frames / Packet.Rate);
                        }
                        expectedEnd = start + 1000.0 * frames / Packet.Rate;
                        short[] samples = new short[checked((int)frames * Packet.Channels)];
                        if ((flags & Silent) == 0) Marshal.Copy(data, samples, 0, samples.Length);
                        packet = new Packet(++sequence, start, samples, flags);
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }

                    lastPacket = Clock.Milliseconds;
                    enqueue(packet with { PacketReadMs = lastPacket });
                    Check(capture.GetNextPacketSize(out available));
                }
            }
        }
        finally
        {
            if (started) _ = client!.Stop();
            if (capture is not null) Marshal.ReleaseComObject(capture);
            if (client is not null) Marshal.ReleaseComObject(client);
            CoUninitialize();
        }
    }

    static void VerifyTarget(Process target, SafeProcessHandle identity, Source source)
    {
        if (WaitForSingleObject(identity, 0) != 258 || target.StartTime.ToUniversalTime().Ticks != source.Started ||
            !string.Equals(target.ProcessName, "Spotify", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Spotify closed or restarted. Refresh sources and start again.");
    }

    static IAudioClient Activate(int pid, CancellationToken stop)
    {
        // Windows x64 PROPVARIANT followed by AUDIOCLIENT_ACTIVATION_PARAMS.
        const int VariantBytes = 24, BlobBytes = 12;
        IntPtr parameters = Marshal.AllocHGlobal(VariantBytes + BlobBytes);
        IntPtr blob = IntPtr.Add(parameters, VariantBytes);
        var callback = new ActivationHandler(parameters);
        IntPtr operation = IntPtr.Zero;
        bool submitted = false;
        try
        {
            Marshal.WriteInt32(blob, 0, 1); // Process loopback activation
            Marshal.WriteInt32(blob, 4, pid);
            Marshal.WriteInt32(blob, 8, 0); // Include target process tree
            var variant = new ActivationVariant { Type = 65, BlobSize = BlobBytes, Blob = blob }; // VT_BLOB
            Marshal.StructureToPtr(variant, parameters, false);
            Guid iid = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, parameters, callback, out operation));
            submitted = true;

            IntPtr result;
            try { result = callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(6), stop).GetAwaiter().GetResult(); }
            catch
            {
                // If completion wins the race with cancellation, claim its pointer here.
                if (!callback.Result.TrySetCanceled() && callback.Result.Task.IsCompletedSuccessfully)
                    Marshal.Release(callback.Result.Task.Result);
                throw;
            }
            try { return (IAudioClient)Marshal.GetObjectForIUnknown(result); }
            finally { Marshal.Release(result); }
        }
        finally
        {
            if (!submitted) callback.FreeParameters();
            if (operation != IntPtr.Zero) Marshal.Release(operation);
            GC.KeepAlive(callback);
        }
    }

    static void Check(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage;
        public int Pid;
        public UIntPtr Heap;
        public uint Module, Threads;
        public int Parent, Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Image;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)] // Windows x64 PROPVARIANT layout
    struct ActivationVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint BlobSize;
        [FieldOffset(16)] public IntPtr Blob;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormat
    {
        public ushort Tag, Channels;
        public uint Rate, AverageBytes;
        public ushort BlockAlign, Bits, Extra;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32First(SafeFileHandle handle, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32Next(SafeFileHandle handle, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, IntPtr parameters,
        [MarshalAs(UnmanagedType.Interface)] IActivationHandler handler, out IntPtr operation);

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long duration, long periodicity, ref WaveFormat format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpc);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivationOperation
    {
        [PreserveSig] int GetActivateResult(out int hr, out IntPtr result);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ActivationHandler(IntPtr parameters) : IActivationHandler, IAgileObject
    {
        public TaskCompletionSource<IntPtr> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void FreeParameters() => Marshal.FreeHGlobal(Interlocked.Exchange(ref parameters, IntPtr.Zero));

        public int ActivateCompleted(IntPtr operation)
        {
            IntPtr result = IntPtr.Zero;
            try
            {
                var op = (IActivationOperation)Marshal.GetObjectForIUnknown(operation);
                try
                {
                    Check(op.GetActivateResult(out int hr, out result));
                    Check(hr);
                    if (result == IntPtr.Zero) throw new InvalidOperationException("WASAPI returned no audio client.");
                }
                finally { Marshal.ReleaseComObject(op); }
                if (Result.TrySetResult(result)) result = IntPtr.Zero;
            }
            catch (Exception ex) { Result.TrySetException(ex); }
            finally
            {
                if (result != IntPtr.Zero) Marshal.Release(result);
                FreeParameters();
            }
            return 0;
        }
    }
}

[ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IActivationHandler
{
    [PreserveSig] int ActivateCompleted(IntPtr operation);
}

[ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAgileObject { }
