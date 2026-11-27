using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

/// <summary>23.1: the Recycle Bin keeps, for each item, a "$I" file with the moment it was deleted.</summary>
public static class RecycleBin
{
    /// <summary>
    /// Deletion time stored in a $I file: version (8 bytes, 1 or 2), original size (8), FILETIME of the deletion (8), path.
    /// Null when the file is not in that format.
    /// </summary>
    public static DateTime? DeletedAtUtc(ReadOnlySpan<byte> info)
    {
        if (info.Length < 24)
            return null;
        var version = BinaryPrimitives.ReadInt64LittleEndian(info);
        if (version is not (1 or 2))
            return null;
        var fileTime = BinaryPrimitives.ReadInt64LittleEndian(info[16..]);
        return fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc() ? null : DateTime.FromFileTimeUtc(fileTime);
    }

    /// <summary>"$IABC123.txt" → "$RABC123.txt" (the deleted item itself, a file or a folder).</summary>
    public static string? ContentName(string infoName) =>
        infoName.StartsWith("$I", StringComparison.OrdinalIgnoreCase) && infoName.Length > 2 ? "$R" + infoName[2..] : null;

    public static bool IsExpired(DateTime deletedUtc, DateTime nowUtc) => nowUtc - deletedUtc >= TimeSpan.FromDays(CleanupTargets.RecycleBinDays);
}

/// <summary>23.3: weekly warning when the graphics driver is old. Never installs anything.</summary>
public static class DriverCheck
{
    public const int MaxAgeDays = 180;

    public static IReadOnlyList<string> Outdated(HardwareSnapshot pc, DateOnly today) => pc.Gpus
        .Where(gpu => !gpu.IsIntegrated && gpu.DriverDate is { } date && today.DayNumber - date.DayNumber > MaxAgeDays)
        .Select(gpu => $"{gpu.Name}: driver {gpu.DriverVersion ?? "?"} de {gpu.DriverDate!.Value:dd/MM/yyyy} ({today.DayNumber - gpu.DriverDate!.Value.DayNumber} dias). " +
            (gpu.Vendor switch
            {
                GpuVendor.Nvidia => "Atualize pelo app da NVIDIA ou em nvidia.com/drivers.",
                GpuVendor.Amd => "Atualize pelo AMD Software ou em amd.com/support.",
                GpuVendor.Intel => "Atualize pelo Intel Driver & Support Assistant.",
                _ => "Atualize pelo site do fabricante."
            }))
        .ToArray();
}

/// <summary>23.4: the weekly network task only switches DNS for someone who already chose "DNS mais rápido".</summary>
public static class DnsAutoSwitch
{
    public const double MinGainMs = 10;

    /// <summary>The faster resolver to switch to, or null to keep the current one.</summary>
    public static DnsResult? Choose(IReadOnlyList<DnsResult> results, string[] currentServers)
    {
        var current = results.Where(result => currentServers.Contains(result.Server)).MinBy(result => result.MedianMs);
        var best = results.Where(result => result.Failures == 0 && !double.IsInfinity(result.MedianMs) && result.Name != "Atual")
            .MinBy(result => result.MedianMs);
        if (best is null || currentServers.Contains(best.Server))
            return null;
        return current is null || current.Failures > 0 || current.MedianMs - best.MedianMs >= MinGainMs ? best : null;
    }
}

/// <summary>23.5: the power supply cannot be detected, so the user may tell it; used in the stability advice.</summary>
public static partial class PowerSupply
{
    /// <summary>Recommended PSU (W) for the most demanding cards, from the vendors' specifications. Null when unknown.</summary>
    public static int? Recommended(string gpuName)
    {
        foreach (var (pattern, watts) in Table)
            if (Regex.IsMatch(gpuName, pattern, RegexOptions.IgnoreCase))
                return watts;
        return null;
    }

    private static readonly (string Pattern, int Watts)[] Table =
    [
        (@"RTX\s?5090", 1000), (@"RTX\s?5080", 850), (@"RTX\s?5070\s?Ti", 750), (@"RTX\s?5070\b", 650),
        (@"RTX\s?4090", 850), (@"RTX\s?4080", 750), (@"RTX\s?4070\s?Ti", 700), (@"RTX\s?4070\b", 650),
        (@"RTX\s?3090", 750), (@"RTX\s?3080", 750), (@"RTX\s?3070", 650),
        (@"RX\s?7900\s?XTX", 800), (@"RX\s?7900", 750), (@"RX\s?9070\s?XT", 750), (@"RX\s?9070\b", 650),
        (@"RX\s?7800", 700), (@"RX\s?6950", 850), (@"RX\s?6900|RX\s?6800", 750)
    ];

    public static Finding? Evaluate(HardwareSnapshot pc, PcSettings settings)
    {
        if (settings.PsuWatts is not { } watts)
            return null;
        var gpu = pc.Gpus.Where(item => !item.IsIntegrated).Select(item => (item.Name, Watts: Recommended(item.Name)))
            .Where(item => item.Watts is not null).MaxBy(item => item.Watts);
        if (gpu.Watts is not { } needed || watts >= needed)
            return null;
        return new Finding("psu-undersized", Severity.Warning, $"Fonte de {watts} W abaixo do recomendado para a {gpu.Name} ({needed} W)",
            "Fonte no limite causa desligamentos sob carga (Kernel-Power 41 sem tela azul) e travadas em picos de consumo.",
            "Não faça overclock nem ligue PBO até trocar a fonte. Se o PC desliga sozinho em jogos, a fonte é a primeira suspeita.",
            "Informado pelo usuário (settings.json: pc.psuWatts)");
    }
}

// ---------------------------------------------------------------- Quick bottleneck test (23.6)

public enum BenchComponent { Memory, Disk }

/// <summary>Expected minimum for a component class (data/baselines.json). Below half of it is flagged.</summary>
public sealed record Baseline(BenchComponent Component, string Class, double ExpectedMbps, string Hint);

/// <param name="MemoryMbps">Large-buffer copy bandwidth.</param>
/// <param name="DiskMbps">Sequential uncached read of the system drive.</param>
/// <param name="CpuSingleScore">Hashing throughput of one thread (MB/s), shown for comparison between runs.</param>
public sealed record QuickBenchResult(double CpuSingleScore, double CpuMultiScore, double MemoryMbps, double DiskMbps);

public static class QuickBench
{
    public const double FlagBelow = 0.5;

    /// <summary>Memory class: DDR4/DDR5 and how many modules (one module is single channel).</summary>
    public static string MemoryClass(HardwareSnapshot pc)
    {
        var modules = pc.Memory?.Modules ?? [];
        var kind = modules.Any(module => module.Kind == MemoryKind.Ddr5) ? "ddr5" : "ddr4";
        return $"{kind}-{(modules.Count <= 1 ? "single" : "dual")}";
    }

    /// <summary>Disk class of the drive Windows is on: nvme, sata-ssd or hdd.</summary>
    public static string? DiskClass(HardwareSnapshot pc)
    {
        var disk = pc.Disks.FirstOrDefault(item => item.IsSystem);
        return disk switch
        {
            null => null,
            { Media: DiskMedia.Hdd } => "hdd",
            _ when disk.Bus.Contains("NVMe", StringComparison.OrdinalIgnoreCase) => "nvme",
            { Media: DiskMedia.Ssd } => "sata-ssd",
            _ => null
        };
    }

    public static IReadOnlyList<Finding> Evaluate(QuickBenchResult result, HardwareSnapshot pc, IReadOnlyList<Baseline> baselines)
    {
        var findings = new List<Finding>();
        void Check(BenchComponent component, string? klass, double measured, string id, string title)
        {
            if (klass is null || baselines.FirstOrDefault(item => item.Component == component && item.Class == klass) is not { } baseline)
                return;
            if (measured < baseline.ExpectedMbps * FlagBelow)
                findings.Add(new Finding(id, Severity.Warning, $"{title}: {measured / 1000:0.0} GB/s (esperado ~{baseline.ExpectedMbps / 1000:0.0} GB/s)",
                    $"Rendeu menos da metade do esperado para {klass}.", baseline.Hint, "Teste rápido de gargalo (NOVAIS boost)"));
        }
        Check(BenchComponent.Memory, MemoryClass(pc), result.MemoryMbps, "memory-slow", "Memória lenta");
        Check(BenchComponent.Disk, DiskClass(pc), result.DiskMbps, "disk-slow", "Disco do Windows lento");
        return findings;
    }
}
