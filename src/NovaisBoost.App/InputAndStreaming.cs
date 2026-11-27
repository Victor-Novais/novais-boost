using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// F19 mouse polling test: a hidden message-only window receives raw mouse input (RIDEV_INPUTSINK) for a few seconds while
/// the user moves the mouse. Read-only; nothing is installed or hooked into other programs.
/// </summary>
internal static partial class MousePolling
{
    private const uint WmInput = 0x00FF;
    private const uint RideVInputSink = 0x00000100;
    private static readonly IntPtr MessageOnly = new(-3);

    public static IReadOnlyList<double> Record(TimeSpan duration)
    {
        var timestamps = new List<double>(65_536);
        var thread = new Thread(() =>
        {
            var window = CreateWindowExW(0, "STATIC", "NOVAIS boost polling", 0, 0, 0, 0, 0, MessageOnly, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero)
                return;
            try
            {
                var device = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RideVInputSink, Target = window };
                if (!RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<RawInputDevice>()))
                    return;
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed < duration)
                {
                    while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                    {
                        if (message.Message == WmInput)
                            timestamps.Add(clock.Elapsed.TotalMilliseconds);
                        DispatchMessageW(ref message);
                    }
                    MsgWaitForMultipleObjects(0, IntPtr.Zero, false, 20, 0x04FF /* QS_ALLINPUT */);
                }
            }
            finally
            {
                DestroyWindow(window);
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest };
        thread.Start();
        thread.Join(duration + TimeSpan.FromSeconds(3));
        return timestamps;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    private static partial IntPtr DispatchMessageW(ref Msg message);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjects(uint count, IntPtr handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds, uint wakeMask);
}

/// <summary>F19 network: upload speed for the streaming bitrate, and latency to the games' servers.</summary>
internal static class GameNetwork
{
    /// <summary>Uploads ~25 MB of random data to Cloudflare's speed test endpoint and returns Mbps. Nothing personal is sent.</summary>
    public static double UploadMbps()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NOVAIS-boost/1.0");
        var payload = new byte[25_000_000];
        Random.Shared.NextBytes(payload);
        var watch = Stopwatch.StartNew();
        using var response = http.PostAsync("https://speed.cloudflare.com/__up", new ByteArrayContent(payload)).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return payload.Length * 8 / 1_000_000d / watch.Elapsed.TotalSeconds;
    }

    /// <summary>TCP connection time to a cloud region (handshake ≈ one round trip), 5 samples.</summary>
    public static PingStats Tcp(string host, int port, int count = 5)
    {
        var samples = new List<double?>();
        for (var index = 0; index < count; index++)
        {
            using var client = new TcpClient();
            var watch = Stopwatch.StartNew();
            try
            {
                if (client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(2)))
                    samples.Add(watch.Elapsed.TotalMilliseconds);
                else
                    samples.Add(null);
            }
            catch (AggregateException)
            {
                samples.Add(null);
            }
            Thread.Sleep(100);
        }
        return NetworkAnalysis.Ping(samples);
    }

    /// <summary>Valve relays: ICMP ping to the first relay of every POP, in parallel; the closest ones first.</summary>
    public static IReadOnlyList<(string Name, PingStats Stats)> ValveRelays(int appId)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NOVAIS-boost/1.0");
        var pops = GameServerCatalog.ParseSdr(http.GetStringAsync(GameServerCatalog.SdrUrl(appId)).GetAwaiter().GetResult());
        var results = new System.Collections.Concurrent.ConcurrentBag<(string, PingStats)>();
        Parallel.ForEach(pops, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            pop => results.Add(($"{pop.Description} ({pop.Code})", NetworkTools.Ping(pop.Address, 4, 150))));
        return GameServerCatalog.Rank(results);
    }
}
