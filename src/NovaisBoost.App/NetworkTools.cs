using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal static class LatencyProbe
{
    /// <summary>Samples DPC/ISR load for the whole system (all processors) plus the current timer resolution.</summary>
    public static LatencySample Measure(TimeSpan duration, CancellationToken cancel = default)
    {
        var samples = new List<(double Dpc, double Isr, double DpcRate, double IsrRate)>();
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end && !cancel.IsCancellationRequested)
        {
            samples.AddRange(Wmi.Query(
                "SELECT PercentDPCTime, PercentInterruptTime, DPCsQueuedPersec, InterruptsPersec FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'",
                item => (Convert.ToDouble(item["PercentDPCTime"]), Convert.ToDouble(item["PercentInterruptTime"]),
                    Convert.ToDouble(item["DPCsQueuedPersec"]), Convert.ToDouble(item["InterruptsPersec"]))));
            cancel.WaitHandle.WaitOne(500);
        }
        // The first formatted sample has no previous value to diff against.
        var valid = samples.Count > 1 ? samples.Skip(1).ToList() : samples;
        if (valid.Count == 0)
            throw new InvalidOperationException("Não foi possível ler os contadores de DPC/interrupções.");

        NtQueryTimerResolution(out _, out _, out var current);
        return new LatencySample(valid.Average(s => s.Dpc), valid.Max(s => s.Dpc), valid.Average(s => s.Isr), valid.Max(s => s.Isr),
            valid.Average(s => s.DpcRate), valid.Average(s => s.IsrRate), current / 10_000d);
    }

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryTimerResolution(out uint minimum, out uint maximum, out uint current);
}

internal static class NetworkTools
{
    public static readonly (string Name, string Primary, string Secondary)[] PublicResolvers =
    [
        ("Cloudflare", "1.1.1.1", "1.0.0.1"),
        ("Google", "8.8.8.8", "8.8.4.4"),
        ("Quad9", "9.9.9.9", "149.112.112.112"),
        ("OpenDNS", "208.67.222.222", "208.67.220.220")
    ];

    private static readonly string[] Domains =
        ["steampowered.com", "riotgames.com", "epicgames.com", "battle.net", "discord.com", "twitch.tv", "youtube.com", "google.com"];

    public static TweakDefinition DnsTweak(Guid adapterId, string adapterName, (string Name, string Primary, string Secondary) resolver) =>
        new("dns-fastest", $"Usar o DNS {resolver.Name}",
            $"Troca o DNS de {adapterName} para {resolver.Primary} e {resolver.Secondary}, o mais rápido no teste.",
            "Rede", TweakLevel.Safe, TweakEvidence.Documented, "Nenhum. Reverter volta ao DNS anterior (automático ou manual).", false, new TweakCompatibility([], []),
            [new DnsAction(adapterId, adapterName, [resolver.Primary, resolver.Secondary])]);

    public static NetworkInterface? ActiveAdapter() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet &&
            adapter.GetIPProperties().GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork))
        .OrderByDescending(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
        .FirstOrDefault();

    public static IPAddress? Gateway(NetworkInterface adapter) => adapter.GetIPProperties().GatewayAddresses
        .Select(gateway => gateway.Address).FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);

    public static PingStats Ping(string host, int count, int intervalMs = 200, CancellationToken cancel = default)
    {
        using var ping = new Ping();
        var results = new List<double?>();
        for (var index = 0; index < count && !cancel.IsCancellationRequested; index++)
        {
            try
            {
                var reply = ping.Send(host, 1000);
                results.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (PingException)
            {
                results.Add(null);
            }
            cancel.WaitHandle.WaitOne(intervalMs);
        }
        return NetworkAnalysis.Ping(results);
    }

    /// <summary>Pings 1.1.1.1 idle and while downloading from Cloudflare's speed test endpoint.</summary>
    public static (PingStats Idle, PingStats Loaded, double DownloadMbps) Bufferbloat()
    {
        var idle = Ping("1.1.1.1", 20, 100);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        long bytes = 0;
        var watch = Stopwatch.StartNew();
        var download = Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NOVAIS-boost/1.0");
            try
            {
                await using var stream = await http.GetStreamAsync("https://speed.cloudflare.com/__down?bytes=500000000", cancel.Token);
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancel.Token)) > 0)
                    Interlocked.Add(ref bytes, read);
            }
            catch (Exception error) when (error is OperationCanceledException or HttpRequestException or IOException)
            {
                // The download is only load generation; stopping it early is expected.
            }
        });
        cancel.Token.WaitHandle.WaitOne(1500); // let the download ramp up
        var loaded = Ping("1.1.1.1", 30, 100);
        var seconds = watch.Elapsed.TotalSeconds;
        cancel.Cancel();
        download.Wait(TimeSpan.FromSeconds(5));
        return (idle, loaded, Interlocked.Read(ref bytes) * 8 / 1_000_000d / Math.Max(seconds, 0.1));
    }

    public static IReadOnlyList<DnsResult> BenchmarkDns(NetworkInterface? adapter)
    {
        var servers = new List<(string Name, string Address)>();
        if (adapter is not null)
            servers.AddRange(adapter.GetIPProperties().DnsAddresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => ("Atual", address.ToString())));
        servers.AddRange(PublicResolvers.Where(resolver => servers.All(server => server.Address != resolver.Primary))
            .Select(resolver => (resolver.Name, resolver.Primary)));

        var results = new List<DnsResult>();
        foreach (var (name, address) in servers)
        {
            var times = new List<double>();
            var failures = 0;
            foreach (var domain in Domains)
            {
                var elapsed = Query(address, domain);
                if (elapsed is { } ms)
                    times.Add(ms);
                else
                    failures++;
            }
            var sorted = times.Order().ToArray();
            results.Add(new DnsResult(name, address, sorted.Length == 0 ? double.PositiveInfinity : sorted[sorted.Length / 2], failures));
        }
        return results.OrderBy(result => result.Failures).ThenBy(result => result.MedianMs).ToArray();
    }

    private static double? Query(string server, string domain)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveTimeout = 1000 };
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = NetworkAnalysis.BuildQuery(id, domain);
        var buffer = new byte[512];
        var watch = Stopwatch.StartNew();
        try
        {
            socket.SendTo(query, new IPEndPoint(IPAddress.Parse(server), 53));
            var length = socket.Receive(buffer);
            return NetworkAnalysis.IsValidReply(buffer.AsSpan(0, length), id) ? watch.Elapsed.TotalMilliseconds : null;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}

/// <summary>IPv4 DNS servers per adapter. Reads the registry; writes with netsh (by interface index).</summary>
internal sealed class WindowsDnsSettings : IDnsSettings
{
    public DnsState Read(DnsAction action)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{action.InterfaceId:B}");
        var servers = (key?.GetValue("NameServer") as string ?? "")
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new DnsState(servers);
    }

    public void Write(DnsAction action, DnsState state)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(nic => Guid.TryParse(nic.Id, out var id) && id == action.InterfaceId)
            ?? throw new InvalidOperationException($"Adaptador {action.Alias} não encontrado.");
        var index = adapter.GetIPProperties().GetIPv4Properties().Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (state.Servers.Length == 0)
            WindowsCommand.Run("netsh.exe", "interface", "ipv4", "set", "dnsservers", $"name={index}", "source=dhcp");
        else
        {
            WindowsCommand.Run("netsh.exe", "interface", "ipv4", "set", "dnsservers", $"name={index}", "source=static", $"address={state.Servers[0]}", "register=primary", "validate=no");
            for (var position = 1; position < state.Servers.Length; position++)
                WindowsCommand.Run("netsh.exe", "interface", "ipv4", "add", "dnsservers", $"name={index}", $"address={state.Servers[position]}",
                    $"index={position + 1}", "validate=no");
        }
        WindowsCommand.Run("ipconfig.exe", "/flushdns");
    }
}
