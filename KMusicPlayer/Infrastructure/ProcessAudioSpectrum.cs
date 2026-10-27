using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace KMusicPlayer.Infrastructure;

/// <summary>WASAPI process-loopback: captures only this app and its audio children, after mixing.</summary>
[SupportedOSPlatform("windows10.0.20348")]
public sealed class ProcessAudioSpectrum : IDisposable
{
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private readonly FftSpectrumAnalyzer _analyzer = new();
    private int _disposed;
    public bool IsRunning { get; private set; }
    public string? Failure { get; private set; }
    public long FramesPublished => _analyzer.FramesPublished;
    public long Packets { get; private set; }
    public long Discontinuities { get; private set; }
    public double PeakSample { get; private set; }
    public IReadOnlyList<double> Levels => _analyzer.Levels;
    public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ProcessAudioSpectrum()
    {
        _thread = new Thread(Capture) { IsBackground = true, Name = "Musik spectrum capture" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Capture()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IActivateOperation? operation = null;
        var parameters = Marshal.AllocCoTaskMem(12);
        var initialized = CoInitializeEx(IntPtr.Zero, 0) >= 0;
        try
        {
            Marshal.WriteInt32(parameters, 0, 1); // PROCESS_LOOPBACK
            Marshal.WriteInt32(parameters, 4, Environment.ProcessId);
            Marshal.WriteInt32(parameters, 8, 0); // INCLUDE_TARGET_PROCESS_TREE
            var variant = new BlobVariant { Type = 65, Size = 12, Data = parameters };
            var handler = new CompletionHandler();
            var clientId = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref clientId, ref variant, handler, out operation));
            if (!handler.Result.Task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Audio capture activation timed out.");
            client = handler.Result.Task.GetAwaiter().GetResult();
            var format = new WaveFormat { Tag = 3, Channels = 2, Rate = FftSpectrumAnalyzer.SampleRate,
                BytesPerSecond = FftSpectrumAnalyzer.SampleRate * 8, BlockAlign = 8, Bits = 32 };
            Check(client.Initialize(0, 0x80060000, 0, 0, ref format, IntPtr.Zero));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureId, out var pointer));
            try { capture = (IAudioCaptureClient)Marshal.GetObjectForIUnknown(pointer); }
            finally { Marshal.Release(pointer); }
            using var samplesReady = new AutoResetEvent(false);
            Check(client.SetEventHandle(samplesReady.SafeWaitHandle.DangerousGetHandle()));
            Check(client.Start());
            IsRunning = true;
            Ready.TrySetResult(true);
            var events = new WaitHandle[] { _stop, samplesReady };
            var samples = new float[8192];
            while (WaitHandle.WaitAny(events, 100) != 0)
            {
                Check(capture.GetNextPacketSize(out var packet));
                while (packet > 0)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try
                    {
                        var count = checked((int)frames * 2);
                        if (samples.Length < count) samples = new float[count];
                        if ((flags & 2) != 0) Array.Clear(samples, 0, count);
                        else Marshal.Copy(data, samples, 0, count);
                        Packets++;
                        var peak = 0d;
                        for (var i = 0; i < count; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
                        PeakSample = peak;
                        if ((flags & 1) != 0) Discontinuities++;
                        if ((flags & 1) != 0) _analyzer.Reset(); // Discontinuity: discard the old FFT window.
                        _analyzer.Add(samples.AsSpan(0, count));
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out packet));
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure = exception.Message;
            Ready.TrySetResult(false);
        }
        finally
        {
            IsRunning = false;
            if (client is not null) client.Stop();
            if (capture is not null) Marshal.ReleaseComObject(capture);
            if (client is not null) Marshal.ReleaseComObject(client);
            if (operation is not null) Marshal.ReleaseComObject(operation);
            Marshal.FreeCoTaskMem(parameters);
            if (initialized) CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Set();
        if (_thread.Join(TimeSpan.FromSeconds(6))) _stop.Dispose();
    }

    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public int Size;
        [FieldOffset(16)] public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WaveFormat
    {
        public ushort Tag, Channels;
        public int Rate, BytesPerSecond;
        public ushort BlockAlign, Bits, Extra;
    }

    [ComVisible(true), Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }

    [ComVisible(true), Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ICompletionHandler
    {
        [PreserveSig] int ActivateCompleted(IActivateOperation operation);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class CompletionHandler : ICompletionHandler, IAgileObject
    {
        internal TaskCompletionSource<IAudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IActivateOperation operation)
        {
            try
            {
                Check(operation.GetActivateResult(out var result, out var pointer));
                try { Check(result); Result.TrySetResult((IAudioClient)Marshal.GetObjectForIUnknown(pointer)); }
                finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
            }
            catch (Exception exception) { Result.TrySetException(exception); }
            return 0;
        }
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateOperation
    {
        [PreserveSig] int GetActivateResult(out int result, out IntPtr activatedInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int mode, uint flags, long duration, long period, ref WaveFormat format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long period, out long minimum);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, out IntPtr service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong timestamp);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    [DllImport("mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, ref BlobVariant parameters,
        ICompletionHandler completion, out IActivateOperation operation);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
