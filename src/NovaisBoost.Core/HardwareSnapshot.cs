namespace NovaisBoost.Core;

public interface IHardwareProbe
{
    HardwareSnapshot Capture();
}

public enum MemoryKind { Unknown, Ddr3, Ddr4, Ddr5 }
public enum DiskMedia { Unknown, Hdd, Ssd }
public enum DiskHealth { Unknown, Healthy, Warning, Unhealthy }
public enum GpuVendor { Other, Nvidia, Amd, Intel }
public enum DisplayConnection { Unknown, Hdmi, DisplayPort, Dvi, Internal, Other }

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
    IReadOnlyList<string> ProbeErrors,
    DeepInfo? Deep = null,
    IReadOnlyList<DataSource>? Sources = null);

/// <summary>Where a piece of the snapshot came from (WMI class, registry key, event log...), saved in the JSON report.</summary>
public sealed record DataSource(string Area, string Source);

/// <summary>Data for the deep diagnosis (F13). Null fields could not be read (no permission, no driver, old Windows).</summary>
/// <param name="TrimEnabled">From "fsutil behavior query DisableDeleteNotify" (NTFS). Needs administrator.</param>
/// <param name="WindowedVrr">The Windows "Variable refresh rate" graphics setting (VRROptimizeEnable); null when never set.</param>
/// <param name="Overlays">Overlay programs running now (names from <see cref="KnownSoftware.Overlays"/>).</param>
/// <param name="Optimizers">Other PC optimizers installed or running (names from data/optimizers.json).</param>
public sealed record DeepInfo(
    SystemEvents? Events = null,
    uint? MicrocodeRevision = null,
    PagefileInfo? Pagefile = null,
    bool? TrimEnabled = null,
    bool? WindowedVrr = null,
    IReadOnlyList<GameInstall>? Games = null,
    IReadOnlyList<string>? Overlays = null,
    IReadOnlyList<string>? Optimizers = null);

/// <summary>Event log counts over the last <see cref="Days"/> days.</summary>
/// <param name="WheaFatal">WHEA-Logger 18: fatal hardware error (machine check).</param>
/// <param name="WheaCorrected">WHEA-Logger 17, 19, 47: corrected PCIe, CPU and memory errors.</param>
/// <param name="BugChecks">WER-SystemErrorReporting 1001: blue screens.</param>
/// <param name="UnexpectedShutdowns">Kernel-Power 41: reboots without a clean shutdown (includes blue screens).</param>
/// <param name="Minidumps">Files in %SystemRoot%\Minidump from the same period; null without administrator.</param>
/// <param name="DisplayResets">Display 4101 (TDR: the video driver stopped responding and recovered).</param>
/// <param name="NvidiaDriverErrors">nvlddmkm 13, 14 and 153.</param>
public sealed record SystemEvents(int Days, int WheaFatal, int WheaCorrected, int BugChecks, int UnexpectedShutdowns,
    int? Minidumps, int DisplayResets, int NvidiaDriverErrors);

/// <param name="Automatic">Win32_ComputerSystem.AutomaticManagedPagefile.</param>
/// <param name="Files">Paging files in use (Win32_PageFileUsage).</param>
public sealed record PagefileInfo(bool Automatic, IReadOnlyList<PagefileUsage> Files);

public sealed record PagefileUsage(string Path, long AllocatedMb);

/// <param name="Media">Media of the disk that holds the game folder.</param>
public sealed record GameInstall(string Name, string Path, DiskMedia Media);

/// <summary>PCIe link of a device. Speed is the generation (1 = 2.5 GT/s ... 5 = 32 GT/s).</summary>
public sealed record PcieLink(int CurrentWidth, int MaxWidth, int CurrentGeneration, int MaxGeneration);

/// <summary>Instant reading from NVML (NVIDIA driver). ClocksEventReasons is the NVML bit mask.</summary>
public sealed record GpuSensors(int TemperatureC, int UtilizationPercent, ulong ClocksEventReasons)
{
    public const ulong SwPowerCap = 0x4, HwSlowdown = 0x8, SwThermalSlowdown = 0x20, HwThermalSlowdown = 0x40, HwPowerBrakeSlowdown = 0x80;

    public bool ThermalLimited => (ClocksEventReasons & (SwThermalSlowdown | HwThermalSlowdown | HwSlowdown)) != 0;
    public bool PowerLimited => (ClocksEventReasons & (SwPowerCap | HwPowerBrakeSlowdown)) != 0;
}

public sealed record OsInfo(string Caption, string Build, string? DisplayVersion, TimeSpan Uptime, bool IsLaptop, bool OnBattery);

public sealed record CpuInfo(string Name, string Manufacturer, int Cores, int Threads, int MaxClockMhz);

public sealed record GpuInfo(string Name, string? DriverVersion, DateOnly? DriverDate, long? VramBytes, bool? ResizableBar,
    PcieLink? Pcie = null, GpuSensors? Sensors = null)
{
    public GpuVendor Vendor => GpuClassifier.Vendor(Name);
    public bool IsIntegrated => GpuClassifier.IsIntegrated(Name);
}

public sealed record MemoryInfo(long TotalBytes, long AvailableBytes, IReadOnlyList<MemoryModule> Modules, long NonPagedPoolBytes = 0)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : 100d * (TotalBytes - AvailableBytes) / TotalBytes;
}

/// <param name="Slot">Win32_PhysicalMemory.DeviceLocator ("DIMM_A2", "ChannelA-DIMM1"...).</param>
/// <param name="Bank">Win32_PhysicalMemory.BankLabel ("BANK 0", "P0 CHANNEL A"...).</param>
public sealed record MemoryModule(
    string Slot, long CapacityBytes, int RatedMts, int ConfiguredMts, MemoryKind Kind, string? Manufacturer, string? PartNumber,
    string? Bank = null);

public sealed record BoardInfo(string Manufacturer, string Product, string BiosVersion, DateOnly? BiosDate);

public sealed record DiskInfo(string Name, DiskMedia Media, string Bus, DiskHealth Health, long SizeBytes, bool IsSystem);

public sealed record VolumeInfo(string Letter, long TotalBytes, long FreeBytes, bool IsSystem, DiskMedia Media = DiskMedia.Unknown)
{
    public double FreePercent => TotalBytes <= 0 ? 100 : 100d * FreeBytes / TotalBytes;
}

public sealed record DisplayInfo(string Name, int Width, int Height, int CurrentHz, int MaxHz,
    DisplayConnection Connection = DisplayConnection.Unknown);

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
