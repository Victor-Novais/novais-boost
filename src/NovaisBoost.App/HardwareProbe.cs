using System.Diagnostics;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Read-only hardware and settings probe. Each area fails independently.</summary>
/// <param name="games">Already detected games, to avoid scanning the launcher folders twice (null = scan here).</param>
internal sealed partial class WindowsHardwareProbe(IActionLog log, Func<IReadOnlyList<InstalledGame>>? games = null) : IHardwareProbe
{
    private readonly List<string> _errors = [];
    private readonly Func<IReadOnlyList<InstalledGame>>? _games = games;

    public HardwareSnapshot Capture()
    {
        _errors.Clear();
        var gpus = Try("GPU", ReadGpus) ?? [];
        var volumes = Try("Volumes", ReadVolumes) ?? [];
        return new HardwareSnapshot(
            DateTimeOffset.Now,
            Try("Windows", ReadOs) ?? new OsInfo("Windows", "?", null, TimeSpan.FromMilliseconds(Environment.TickCount64), false, false),
            Try("CPU", ReadCpu),
            gpus,
            Try("RAM", ReadMemory),
            Try("Placa-mãe", ReadBoard),
            Try("Discos", ReadDisks) ?? [],
            volumes,
            Try("Monitores", ReadDisplays) ?? [],
            Try("Rede", ReadNetworks) ?? [],
            Try("Energia", ReadPowerPlan),
            ReadFlags(),
            Try("Processos", ReadTopProcesses) ?? [],
            _errors.ToArray(),
            ReadDeep(volumes),
            AreaSources);
    }

    private T? Try<T>(string area, Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _errors.Add($"{area}: {error.Message}");
            log.Warning($"Diagnóstico de {area} falhou: {error.Message}");
            return null;
        }
    }

    private static OsInfo ReadOs()
    {
        var (caption, build) = Wmi.Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem",
            item => ((string)item["Caption"], (string)item["BuildNumber"])).Single();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var chassis = Wmi.Query("SELECT ChassisTypes FROM Win32_SystemEnclosure", item => item["ChassisTypes"] as ushort[] ?? []);
        var laptopTypes = new ushort[] { 8, 9, 10, 11, 14, 30, 31, 32 };
        var laptop = chassis.SelectMany(types => types).Any(laptopTypes.Contains);
        var onBattery = Wmi.Query("SELECT BatteryStatus FROM Win32_Battery", item => Convert.ToInt32(item["BatteryStatus"] ?? 0)).Any(status => status == 1);
        return new OsInfo(caption.Trim(), build, key?.GetValue("DisplayVersion") as string,
            TimeSpan.FromMilliseconds(Environment.TickCount64), laptop, onBattery);
    }

    private static CpuInfo ReadCpu()
    {
        var rows = Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor",
            item => new CpuInfo(((string)item["Name"]).Trim(), (string)item["Manufacturer"], Convert.ToInt32(item["NumberOfCores"]),
                Convert.ToInt32(item["NumberOfLogicalProcessors"]), Convert.ToInt32(item["MaxClockSpeed"])));
        return rows[0] with { Cores = rows.Sum(cpu => cpu.Cores), Threads = rows.Sum(cpu => cpu.Threads) };
    }

    private IReadOnlyList<GpuInfo> ReadGpus()
    {
        var vram = ReadVramByName();
        var rebar = Try("Resizable BAR", ReadNvidiaBar1);
        var sensors = Try("Sensores da GPU", Nvml.Read) ?? [];
        return Wmi.Query("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController", item =>
            {
                var name = ((string?)item["Name"] ?? "").Trim();
                var pnp = (string?)item["PNPDeviceID"] ?? "";
                if (!pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) || name.Length == 0)
                    return null;
                DateOnly? date = item["DriverDate"] is string text && text.Length > 0
                    ? DateOnly.FromDateTime(ManagementDateTimeConverter.ToDateTime(text)) : null;
                bool? bar = GpuClassifier.Vendor(name) == GpuVendor.Nvidia && rebar is not null ? rebar.Value : null;
                return new GpuInfo(name, item["DriverVersion"] as string, date, vram.GetValueOrDefault(name), bar,
                    Try("PCIe da GPU", () => ReadPcieLink(pnp)), sensors.GetValueOrDefault(name));
            })
            .OfType<GpuInfo>().ToArray();
    }

    private static Dictionary<string, long?> ReadVramByName()
    {
        var result = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
        using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (display is null)
            return result;
        foreach (var name in display.GetSubKeyNames().Where(name => name.All(char.IsDigit)))
        {
            using var adapter = display.OpenSubKey(name);
            if (adapter?.GetValue("DriverDesc") is not string description)
                continue;
            long? bytes = adapter.GetValue("HardwareInformation.qwMemorySize") switch
            {
                long value => value,
                byte[] { Length: 8 } data => BitConverter.ToInt64(data),
                _ => adapter.GetValue("HardwareInformation.MemorySize") switch
                {
                    int value => unchecked((uint)value),
                    byte[] { Length: 4 } data => BitConverter.ToUInt32(data),
                    _ => null
                }
            };
            if (bytes > 0)
                result[description.Trim()] = bytes;
        }
        return result;
    }

    /// <summary>On NVIDIA, Resizable BAR is active when the BAR1 aperture exceeds the legacy 256 MiB window.</summary>
    private static StrongBox<bool>? ReadNvidiaBar1()
    {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe")))
            return null;
        var match = Bar1Total().Match(WindowsCommand.Run("nvidia-smi.exe", "-q", "-d", "MEMORY"));
        return match.Success ? new StrongBox<bool>(long.Parse(match.Groups[1].Value) > 256) : null;
    }

    private static MemoryInfo ReadMemory()
    {
        var totalKb = Wmi.Query("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem",
            item => Convert.ToInt64(item["TotalVisibleMemorySize"])).Single();
        // Perf class names are not localized, unlike performance counter paths.
        var (availableMb, nonPaged) = Wmi.Query("SELECT AvailableMBytes, PoolNonpagedBytes FROM Win32_PerfFormattedData_PerfOS_Memory",
            item => (Convert.ToInt64(item["AvailableMBytes"]), Convert.ToInt64(item["PoolNonpagedBytes"]))).Single();
        var modules = Wmi.Query("SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, PartNumber FROM Win32_PhysicalMemory",
            item => new MemoryModule(
                ((string?)item["DeviceLocator"] ?? "?").Trim(),
                Convert.ToInt64(item["Capacity"] ?? 0L),
                Convert.ToInt32(item["Speed"] ?? 0),
                Convert.ToInt32(item["ConfiguredClockSpeed"] ?? 0),
                Convert.ToInt32(item["SMBIOSMemoryType"] ?? 0) switch { 24 => MemoryKind.Ddr3, 26 => MemoryKind.Ddr4, 34 => MemoryKind.Ddr5, _ => MemoryKind.Unknown },
                ((string?)item["Manufacturer"])?.Trim(),
                ((string?)item["PartNumber"])?.Trim(),
                ((string?)item["BankLabel"])?.Trim()));
        return new MemoryInfo(totalKb * 1024, availableMb * 1_048_576, modules, nonPaged);
    }

    private static BoardInfo ReadBoard()
    {
        var (maker, product) = Wmi.Query("SELECT Manufacturer, Product FROM Win32_BaseBoard",
            item => (((string?)item["Manufacturer"] ?? "?").Trim(), ((string?)item["Product"] ?? "?").Trim())).First();
        var (version, date) = Wmi.Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS",
            item => (((string?)item["SMBIOSBIOSVersion"] ?? "?").Trim(),
                item["ReleaseDate"] is string text ? DateOnly.FromDateTime(ManagementDateTimeConverter.ToDateTime(text)) : (DateOnly?)null)).First();
        return new BoardInfo(maker, product, version, date);
    }

    private static IReadOnlyList<DiskInfo> ReadDisks()
    {
        const string scope = @"root\Microsoft\Windows\Storage";
        var systemLetter = char.ToUpperInvariant((Environment.GetEnvironmentVariable("SystemDrive") ?? "C:")[0]);
        var systemDisks = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition",
                item => (Letter: char.ToUpperInvariant(Convert.ToChar(item["DriveLetter"] ?? '\0')), Disk: Convert.ToString(item["DiskNumber"])), scope)
            .Where(partition => partition.Letter == systemLetter).Select(partition => partition.Disk).ToHashSet();
        return Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk", item => new DiskInfo(
            ((string?)item["FriendlyName"] ?? "?").Trim(),
            Convert.ToInt32(item["MediaType"] ?? 0) switch { 3 => DiskMedia.Hdd, 4 => DiskMedia.Ssd, _ => DiskMedia.Unknown },
            Convert.ToInt32(item["BusType"] ?? 0) switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", _ => "Outro" },
            Convert.ToInt32(item["HealthStatus"] ?? 99) switch { 0 => DiskHealth.Healthy, 1 => DiskHealth.Warning, 2 => DiskHealth.Unhealthy, _ => DiskHealth.Unknown },
            Convert.ToInt64(item["Size"] ?? 0L),
            systemDisks.Contains(Convert.ToString(item["DeviceId"]))), scope);
    }

    private IReadOnlyList<VolumeInfo> ReadVolumes()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var media = Try("Tipo de mídia dos volumes", ReadVolumeMedia) ?? [];
        return DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
            .Select(drive => new VolumeInfo(drive.Name.TrimEnd('\\'), drive.TotalSize, drive.AvailableFreeSpace,
                string.Equals(drive.Name, system, StringComparison.OrdinalIgnoreCase),
                media.GetValueOrDefault(char.ToUpperInvariant(drive.Name[0])).Media,
                media.GetValueOrDefault(char.ToUpperInvariant(drive.Name[0])).Nvme))
            .ToArray();
    }

    /// <summary>Drive letter → media of the physical disk that holds it, and whether it is NVMe (BusType 17).</summary>
    private static Dictionary<char, (DiskMedia Media, bool Nvme)> ReadVolumeMedia()
    {
        const string scope = @"root\Microsoft\Windows\Storage";
        var disks = Wmi.Query("SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk", item => (Id: Convert.ToString(item["DeviceId"]),
                Media: Convert.ToInt32(item["MediaType"] ?? 0) switch { 3 => DiskMedia.Hdd, 4 => DiskMedia.Ssd, _ => DiskMedia.Unknown },
                Nvme: Convert.ToInt32(item["BusType"] ?? 0) == 17), scope)
            .Where(disk => disk.Id is not null).DistinctBy(disk => disk.Id).ToDictionary(disk => disk.Id!, disk => (disk.Media, disk.Nvme));
        return Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition",
                item => (Letter: char.ToUpperInvariant(Convert.ToChar(item["DriveLetter"] ?? '\0')), Disk: Convert.ToString(item["DiskNumber"])), scope)
            .Where(partition => partition.Letter != '\0' && partition.Disk is not null && disks.ContainsKey(partition.Disk))
            .DistinctBy(partition => partition.Letter)
            .ToDictionary(partition => partition.Letter, partition => disks[partition.Disk!]);
    }

    private static IReadOnlyList<NetworkInfo> ReadNetworks() => Wmi.Query(
            "SELECT Name, InterfaceDescription, MediaConnectState, NdisPhysicalMedium, Speed, HardwareInterface, Virtual FROM MSFT_NetAdapter",
            item =>
            {
                var medium = Convert.ToInt32(item["NdisPhysicalMedium"] ?? 0);
                if (item["HardwareInterface"] is not true || item["Virtual"] is true || medium is not (14 or 9))
                    return null;
                return new NetworkInfo(((string?)item["InterfaceDescription"] ?? (string?)item["Name"] ?? "?").Trim(), medium == 14,
                    Convert.ToInt32(item["MediaConnectState"] ?? 0) == 1, Convert.ToInt64(item["Speed"] ?? 0L));
            }, @"root\StandardCimv2")
        .OfType<NetworkInfo>().ToArray();

    /// <summary>powercfg works without elevation, unlike the Win32_PowerPlan WMI class.</summary>
    private static PowerPlanInfo? ReadPowerPlan()
    {
        var output = WindowsCommand.Run("powercfg.exe", "/getactivescheme");
        var id = GuidPattern().Match(output);
        var name = Regex.Match(output, @"\((.+)\)\s*$");
        return id.Success ? new PowerPlanInfo(Guid.Parse(id.Value), name.Success ? name.Groups[1].Value.Trim() : id.Value) : null;
    }

    private SystemFlags ReadFlags()
    {
        bool? Flag(string area, Func<bool?> read)
        {
            try
            {
                return read();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                _errors.Add($"{area}: {error.Message}");
                return null;
            }
        }

        static object? Value(RegistryKey root, string path, string name)
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name);
        }

        return new SystemFlags(
            Flag("Modo de Jogo", () => Value(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") is not int value || value != 0),
            Flag("HAGS", () => Value(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") switch
            {
                2 => true, 1 => false, _ => null
            }),
            Flag("Inicialização rápida", () => Value(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") is 1),
            Flag("Integridade de memória", () => Wmi.Query("SELECT SecurityServicesRunning FROM Win32_DeviceGuard",
                item => item["SecurityServicesRunning"] as uint[] ?? [], @"root\Microsoft\Windows\DeviceGuard").Any(running => running.Contains(2u))));
    }

    private static IReadOnlyList<ProcessUsage> ReadTopProcesses()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(process =>
                {
                    try
                    {
                        // The WSL/Hyper-V VM process under-reports its working set; private bytes reflect the RAM it holds.
                        var bytes = process.ProcessName.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase)
                            ? Math.Max(process.WorkingSet64, process.PrivateMemorySize64) : process.WorkingSet64;
                        return (process.ProcessName, WorkingSet64: bytes);
                    }
                    catch (InvalidOperationException) { return (process.ProcessName, WorkingSet64: 0L); }
                })
                .Where(process => process.ProcessName is not ("Idle" or "System" or "Memory Compression" or "Registry"))
                .GroupBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ProcessUsage(group.Key, group.Count(), group.Sum(process => process.WorkingSet64)))
                .OrderByDescending(process => process.WorkingSetBytes).Take(8).ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    private IReadOnlyList<DisplayInfo> ReadDisplays()
    {
        const int attachedToDesktop = 0x1;
        const int currentSettings = -1;
        var displays = new List<DisplayInfo>();
        var friendly = ReadMonitorNames();
        var connections = Try("Conexão dos monitores", ReadDisplayConnections) ?? [];
        for (uint index = 0; ; index++)
        {
            var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref adapter, 0))
                break;
            if ((adapter.StateFlags & attachedToDesktop) == 0 || adapter.DeviceName is null)
                continue;

            var current = NewMode();
            if (!EnumDisplaySettings(adapter.DeviceName, currentSettings, ref current))
                continue;
            var max = current.DisplayFrequency;
            for (var mode = 0; ; mode++)
            {
                var candidate = NewMode();
                if (!EnumDisplaySettings(adapter.DeviceName, mode, ref candidate))
                    break;
                if (candidate.PelsWidth == current.PelsWidth && candidate.PelsHeight == current.PelsHeight)
                    max = Math.Max(max, candidate.DisplayFrequency);
            }

            var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            var found = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0);
            var hardwareId = found ? HardwareId(monitor.DeviceId) : null;
            var name = hardwareId is not null && friendly.TryGetValue(hardwareId, out var model) ? model
                : found && !string.IsNullOrWhiteSpace(monitor.DeviceString) ? monitor.DeviceString : adapter.DeviceName;
            displays.Add(new DisplayInfo(name.Trim(), current.PelsWidth, current.PelsHeight, current.DisplayFrequency, max,
                connections.GetValueOrDefault(adapter.DeviceName, DisplayConnection.Unknown)));
        }
        return displays;
    }

    /// <summary>"MONITOR\GSM5BB7\{guid}\0001" and "DISPLAY\GSM5BB7\5&amp;...": the second segment identifies the model.</summary>
    private static string? HardwareId(string? deviceId) => deviceId?.Split('\\') is { Length: >= 2 } parts ? parts[1] : null;

    private static Dictionary<string, string> ReadMonitorNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (instance, name) in Wmi.Query("SELECT InstanceName, UserFriendlyName FROM WmiMonitorID", item =>
                (item["InstanceName"] as string, new string((item["UserFriendlyName"] as ushort[] ?? []).TakeWhile(c => c != 0).Select(c => (char)c).ToArray())),
                @"root\wmi"))
            {
                if (HardwareId(instance) is { } id && name.Trim().Length > 0)
                    names.TryAdd(id, name.Trim());
            }
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException)
        {
            // Friendly names are cosmetic; fall back to the driver's name.
        }
        return names;
    }

    private static DevMode NewMode() => new() { Size = (short)Marshal.SizeOf<DevMode>() };

    [GeneratedRegex(@"BAR1 Memory Usage\s*\r?\n\s*Total\s*:\s*(\d+)\s*MiB")]
    private static partial Regex Bar1Total();

    [GeneratedRegex(@"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TtOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
}
