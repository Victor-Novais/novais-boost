namespace NovaisBoost.Core;

public interface IHardwareProbe
{
    HardwareSnapshot Capture();
}

public enum MemoryKind { Unknown, Ddr3, Ddr4, Ddr5 }
public enum DiskMedia { Unknown, Hdd, Ssd }
public enum DiskHealth { Unknown, Healthy, Warning, Unhealthy }
public enum GpuVendor { Other, Nvidia, Amd, Intel }

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    OsInfo Os,
    CpuInfo? Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    MemoryInfo? Memory,
    BoardInfo? Board,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<VolumeInfo> Volumes,
    IReadOnlyList<DisplayInfo> Displays,
    IReadOnlyList<NetworkInfo> Networks,
    PowerPlanInfo? PowerPlan,
    SystemFlags Flags,
    IReadOnlyList<ProcessUsage> TopProcesses,
    IReadOnlyList<string> ProbeErrors);

public sealed record OsInfo(string Caption, string Build, string? DisplayVersion, TimeSpan Uptime, bool IsLaptop, bool OnBattery);

public sealed record CpuInfo(string Name, string Manufacturer, int Cores, int Threads, int MaxClockMhz);

public sealed record GpuInfo(string Name, string? DriverVersion, DateOnly? DriverDate, long? VramBytes, bool? ResizableBar)
{
    public GpuVendor Vendor => GpuClassifier.Vendor(Name);
    public bool IsIntegrated => GpuClassifier.IsIntegrated(Name);
}

public sealed record MemoryInfo(long TotalBytes, long AvailableBytes, IReadOnlyList<MemoryModule> Modules, long NonPagedPoolBytes = 0)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : 100d * (TotalBytes - AvailableBytes) / TotalBytes;
}

public sealed record MemoryModule(
    string Slot, long CapacityBytes, int RatedMts, int ConfiguredMts, MemoryKind Kind, string? Manufacturer, string? PartNumber);

public sealed record BoardInfo(string Manufacturer, string Product, string BiosVersion, DateOnly? BiosDate);

public sealed record DiskInfo(string Name, DiskMedia Media, string Bus, DiskHealth Health, long SizeBytes, bool IsSystem);

public sealed record VolumeInfo(string Letter, long TotalBytes, long FreeBytes, bool IsSystem)
{
    public double FreePercent => TotalBytes <= 0 ? 100 : 100d * FreeBytes / TotalBytes;
}

public sealed record DisplayInfo(string Name, int Width, int Height, int CurrentHz, int MaxHz);

public sealed record NetworkInfo(string Name, bool Wired, bool Connected, long LinkBitsPerSecond);

public sealed record PowerPlanInfo(Guid Id, string Name)
{
    public static readonly Guid PowerSaver = new("a1841308-3668-4c4d-b0e4-6ad7290c1f1d");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public bool IsWindowsDefault => Id == PowerSaver || Id == Balanced || Id == HighPerformance || Id == UltimatePerformance;
}

/// <summary>Windows settings relevant to gaming. Null means the value could not be read.</summary>
public sealed record SystemFlags(bool? GameMode, bool? HardwareGpuScheduling, bool? FastStartup, bool? MemoryIntegrity);

public sealed record ProcessUsage(string Name, int Instances, long WorkingSetBytes);

public static class GpuClassifier
{
    public static GpuVendor Vendor(string name) =>
        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
        : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
        : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
        : GpuVendor.Other;

    /// <summary>Heuristic based on the driver name: integrated GPUs carry no model number.</summary>
    public static bool IsIntegrated(string name)
    {
        var text = name.Replace("(TM)", "", StringComparison.OrdinalIgnoreCase).Replace("(R)", "", StringComparison.OrdinalIgnoreCase);
        return Vendor(name) switch
        {
            GpuVendor.Amd => !System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(RX|R[579]|Pro|FirePro|Instinct)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            GpuVendor.Intel => !System.Text.RegularExpressions.Regex.IsMatch(text, @"\bArc\s+[AB]\d{3}", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            _ => false
        };
    }
}
