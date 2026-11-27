using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Deep diagnosis collection (F13). Read-only, each item fails independently and is reported in ProbeErrors.</summary>
internal sealed partial class WindowsHardwareProbe
{
    private const int EventDays = 30;

    /// <summary>Where each area of the snapshot comes from; saved in the JSON report.</summary>
    private static readonly DataSource[] AreaSources =
    [
        new("Windows", "Win32_OperatingSystem, Win32_SystemEnclosure, Win32_Battery"),
        new("CPU", "Win32_Processor"),
        new("GPU", "Win32_VideoController, registro da classe de vídeo, nvidia-smi (BAR1)"),
        new("PCIe da GPU", "CM_Get_DevNode_Property: DEVPKEY_PciDevice_CurrentLinkWidth/MaxLinkWidth/CurrentLinkSpeed/MaxLinkSpeed"),
        new("Sensores da GPU", "NVML (nvml.dll do driver NVIDIA)"),
        new("RAM", "Win32_OperatingSystem, Win32_PerfFormattedData_PerfOS_Memory, Win32_PhysicalMemory"),
        new("Placa-mãe", "Win32_BaseBoard, Win32_BIOS"),
        new("Discos", @"root\Microsoft\Windows\Storage: MSFT_PhysicalDisk, MSFT_Partition"),
        new("Volumes", "DriveInfo + MSFT_Partition/MSFT_PhysicalDisk (tipo de mídia)"),
        new("Monitores", "EnumDisplayDevices, EnumDisplaySettings, WmiMonitorID, QueryDisplayConfig (conexão)"),
        new("Rede", @"root\StandardCimv2: MSFT_NetAdapter"),
        new("Energia", "powercfg /getactivescheme"),
        new("Configurações", @"Registro (GameBar, GraphicsDrivers, Session Manager\Power), Win32_DeviceGuard"),
        new("Eventos", "Log do Sistema (WHEA-Logger, WER-SystemErrorReporting, Kernel-Power, Display, nvlddmkm), %SystemRoot%\\Minidump"),
        new("Microcódigo", @"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0 [Update Revision]"),
        new("Paginação", "Win32_ComputerSystem.AutomaticManagedPagefile, Win32_PageFileUsage"),
        new("TRIM", "fsutil behavior query DisableDeleteNotify"),
        new("VRR", @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences [DirectXUserGlobalSettings]"),
        new("Jogos", "GameLibrary (pastas dos launchers) + tipo de mídia do volume"),
        new("Overlays e otimizadores", @"Processos em execução, HKLM/HKCU\...\Uninstall [DisplayName], data/optimizers.json")
    ];

    private DeepInfo ReadDeep(IReadOnlyList<VolumeInfo> volumes)
    {
        var processes = Try("Processos", () => ProcessNames()) ?? [];
        return new DeepInfo(
            Try("Eventos", ReadEvents),
            Try("Microcódigo", () => ReadMicrocode() is { } revision ? new StrongBoxed<uint>(revision) : null)?.Value,
            Try("Paginação", ReadPagefile),
            Try("TRIM", () => ReadTrim() is { } trim ? new StrongBoxed<bool>(trim) : null)?.Value,
            Try("VRR", () => ReadWindowedVrr() is { } vrr ? new StrongBoxed<bool>(vrr) : null)?.Value,
            Try("Jogos", () => ReadGames(volumes)),
            KnownSoftware.MatchOverlays(processes),
            Try("Otimizadores", () => KnownSoftware.MatchOptimizers(DataCatalog.Optimizers(), InstalledPrograms(), processes)));
    }

    private sealed record StrongBoxed<T>(T Value) where T : struct;

    private static string[] ProcessNames()
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Select(process => process.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    // ------------------------------------------------------------------ event log (13.3, 13.4, 13.5)

    private static SystemEvents ReadEvents()
    {
        int Count(string provider, params int[] ids)
        {
            var filter = string.Join(" or ", ids.Select(id => $"EventID={id}"));
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='{provider}'] and ({filter}) and TimeCreated[timediff(@SystemTime) <= {EventDays * 86_400_000L}]]]");
            using var reader = new EventLogReader(query);
            var count = 0;
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                    count++;
            }
            return count;
        }

        int? minidumps = null;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            minidumps = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.dmp").Count(file => DateTime.Now - File.GetLastWriteTime(file) <= TimeSpan.FromDays(EventDays))
                : 0;
        }
        catch (UnauthorizedAccessException)
        {
            // Without administrator the folder is not readable; the event log still counts blue screens.
        }

        return new SystemEvents(EventDays,
            WheaFatal: Count("Microsoft-Windows-WHEA-Logger", 18),
            WheaCorrected: Count("Microsoft-Windows-WHEA-Logger", 17, 19, 47),
            BugChecks: Count("Microsoft-Windows-WER-SystemErrorReporting", 1001),
            UnexpectedShutdowns: Count("Microsoft-Windows-Kernel-Power", 41),
            Minidumps: minidumps,
            DisplayResets: Count("Display", 4101),
            NvidiaDriverErrors: Count("nvlddmkm", 13, 14, 153));
    }

    // ------------------------------------------------------------------ microcode, paging file, TRIM, VRR

    /// <summary>Intel keeps the revision in the high DWORD of the 8-byte value; AMD in the low one.</summary>
    private static uint? ReadMicrocode()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return key?.GetValue("Update Revision") switch
        {
            byte[] { Length: >= 8 } data when BitConverter.ToUInt32(data, 4) != 0 => BitConverter.ToUInt32(data, 4),
            byte[] { Length: >= 4 } data => BitConverter.ToUInt32(data, 0),
            _ => null
        };
    }

    private static PagefileInfo ReadPagefile()
    {
        var automatic = Wmi.Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem", item => item["AutomaticManagedPagefile"] is true).FirstOrDefault();
        var files = Wmi.Query("SELECT Name, AllocatedBaseSize FROM Win32_PageFileUsage",
            item => new PagefileUsage(((string?)item["Name"] ?? "?").Trim(), Convert.ToInt64(item["AllocatedBaseSize"] ?? 0L)));
        return new PagefileInfo(automatic, files);
    }

    private static bool? ReadTrim()
    {
        var match = Regex.Match(WindowsCommand.Run("fsutil.exe", "behavior", "query", "DisableDeleteNotify"), @"NTFS\s+DisableDeleteNotify\s*=\s*(\d)");
        return match.Success ? match.Groups[1].Value == "0" : null;
    }

    private static bool? ReadWindowedVrr()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
        var match = Regex.Match(key?.GetValue("DirectXUserGlobalSettings") as string ?? "", @"VRROptimizeEnable=(\d)");
        return match.Success ? match.Groups[1].Value != "0" : null;
    }

    // ------------------------------------------------------------------ games and software

    private IReadOnlyList<GameInstall> ReadGames(IReadOnlyList<VolumeInfo> volumes) => (_games?.Invoke() ?? GameLibrary.Detect(DataCatalog.Games(), log))
        .Where(game => game.Game.Id != "obs")
        .Select(game => new GameInstall(game.Game.Name, game.ExecutablePath,
            volumes.FirstOrDefault(volume => game.ExecutablePath.StartsWith(volume.Letter, StringComparison.OrdinalIgnoreCase))?.Media ?? DiskMedia.Unknown))
        .DistinctBy(game => game.Name)
        .ToArray();

    private static IEnumerable<string> InstalledPrograms()
    {
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var names = new List<string>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(uninstall);
            if (key is null)
                continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var program = key.OpenSubKey(sub);
                if (program?.GetValue("DisplayName") is string name && name.Trim().Length > 0)
                    names.Add(name.Trim());
            }
        }
        return names;
    }

    // ------------------------------------------------------------------ PCIe link of the GPU (13.1)

    private static readonly Guid PciDeviceProperties = new("3AB22E31-8264-4b4e-9AF5-A8D2D8E33E62");
    private const uint CurrentLinkSpeed = 9, CurrentLinkWidth = 10, MaxLinkSpeed = 11, MaxLinkWidth = 12;

    /// <summary>
    /// Link of the GPU's PCI device. Radeon cards have an internal PCIe switch: the endpoint reports the internal link, so
    /// the topmost bridge of the same vendor (the card's upstream port in the slot) is used instead.
    /// </summary>
    private static PcieLink? ReadPcieLink(string instanceId)
    {
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0)
            return null;
        var vendor = Regex.Match(instanceId, @"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase).Groups[1].Value;
        var slot = node;
        while (CM_Get_Parent(out var parent, slot, 0) == 0 && DeviceId(parent) is { } parentId &&
               parentId.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) &&
               Regex.IsMatch(parentId, $@"VEN_{vendor}\b", RegexOptions.IgnoreCase))
            slot = parent;

        uint? Read(uint pid)
        {
            var key = new DevPropKey { FormatId = PciDeviceProperties, PropertyId = pid };
            var buffer = new byte[4];
            var size = (uint)buffer.Length;
            return CM_Get_DevNode_PropertyW(slot, ref key, out _, buffer, ref size, 0) == 0 && size == 4 ? BitConverter.ToUInt32(buffer) : null;
        }
        return Read(CurrentLinkWidth) is { } width && Read(MaxLinkWidth) is { } max
            ? new PcieLink((int)width, (int)max, (int)(Read(CurrentLinkSpeed) ?? 0), (int)(Read(MaxLinkSpeed) ?? 0))
            : null;
    }

    private static string? DeviceId(uint node)
    {
        var buffer = new char[400];
        return CM_Get_Device_IDW(node, buffer, buffer.Length, 0) == 0 ? new string(buffer).TrimEnd('\0') : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Parent(out uint parent, uint node, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_Device_IDW(uint node, [Out] char[] buffer, int length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_DevNode_PropertyW(uint node, ref DevPropKey key, out uint type, [Out] byte[] buffer, ref uint size, uint flags);

    // ------------------------------------------------------------------ display connection (13.12)

    /// <summary>GDI device name ("\\.\DISPLAY1") → how the monitor is connected, from the active display paths.</summary>
    private static Dictionary<string, DisplayConnection> ReadDisplayConnections()
    {
        const uint onlyActivePaths = 0x2;
        var result = new Dictionary<string, DisplayConnection>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(onlyActivePaths, out var pathCount, out var modeCount) != 0)
            return result;
        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        if (QueryDisplayConfig(onlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            return result;
        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = 1, Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(), AdapterId = path.SourceAdapterId, Id = path.SourceId
                }
            };
            if (DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrEmpty(source.ViewGdiDeviceName))
                continue;
            result[source.ViewGdiDeviceName] = path.OutputTechnology switch
            {
                5 => DisplayConnection.Hdmi,
                10 => DisplayConnection.DisplayPort,
                4 => DisplayConnection.Dvi,
                11 or 13 or 0x80000000 => DisplayConnection.Internal,
                _ => DisplayConnection.Other
            };
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId, SourceModeIndex, SourceFlags;
        public Luid TargetAdapterId;
        public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetFlags, Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType, Id;
        public Luid AdapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type, Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topologyId);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);
}

/// <summary>
/// NVIDIA Management Library from the installed driver (System32\nvml.dll). Read-only sensors; loaded on demand so PCs
/// without an NVIDIA driver are unaffected.
/// </summary>
internal static class Nvml
{
    private delegate int Init();
    private delegate int Shutdown();
    private delegate int GetCount(out uint count);
    private delegate int GetHandle(uint index, out IntPtr device);
    private delegate int GetName(IntPtr device, [Out] byte[] name, uint length);
    private delegate int GetTemperature(IntPtr device, int sensor, out uint celsius);
    private delegate int GetUtilization(IntPtr device, out Utilization rates);
    private delegate int GetReasons(IntPtr device, out ulong reasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu, Memory;
    }

    /// <summary>GPU name → sensors. Empty when there is no NVIDIA driver.</summary>
    public static Dictionary<string, GpuSensors> Read()
    {
        var result = new Dictionary<string, GpuSensors>(StringComparer.OrdinalIgnoreCase);
        if (!NativeLibrary.TryLoad(Path.Combine(Environment.SystemDirectory, "nvml.dll"), out var library))
            return result;
        try
        {
            T Function<T>(params string[] names) where T : Delegate
            {
                foreach (var name in names)
                    if (NativeLibrary.TryGetExport(library, name, out var address))
                        return Marshal.GetDelegateForFunctionPointer<T>(address);
                throw new EntryPointNotFoundException(names[0]);
            }

            if (Function<Init>("nvmlInit_v2")() != 0)
                return result;
            try
            {
                var getHandle = Function<GetHandle>("nvmlDeviceGetHandleByIndex_v2");
                var getName = Function<GetName>("nvmlDeviceGetName");
                var getTemperature = Function<GetTemperature>("nvmlDeviceGetTemperature");
                var getUtilization = Function<GetUtilization>("nvmlDeviceGetUtilizationRates");
                var getReasons = Function<GetReasons>("nvmlDeviceGetCurrentClocksEventReasons", "nvmlDeviceGetCurrentClocksThrottleReasons");
                if (Function<GetCount>("nvmlDeviceGetCount_v2")(out var count) != 0)
                    return result;
                for (uint index = 0; index < count; index++)
                {
                    if (getHandle(index, out var device) != 0)
                        continue;
                    var buffer = new byte[96];
                    if (getName(device, buffer, (uint)buffer.Length) != 0)
                        continue;
                    var name = System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0').Trim();
                    var temperature = getTemperature(device, 0, out var celsius) == 0 ? (int)celsius : 0;
                    var utilization = getUtilization(device, out var rates) == 0 ? (int)rates.Gpu : 0;
                    var reasons = getReasons(device, out var mask) == 0 ? mask : 0;
                    result.TryAdd(name, new GpuSensors(temperature, utilization, reasons));
                }
            }
            finally
            {
                Function<Shutdown>("nvmlShutdown")();
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
        return result;
    }
}

/// <summary>Optional 20 s all-core load (13.8) while sampling the processor performance counters. Changes nothing.</summary>
internal static class CpuLoadTest
{
    public static CpuLoadSample Run(TimeSpan duration, CancellationToken cancel = default)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var workers = Enumerable.Range(0, Environment.ProcessorCount).Select(_ => new Thread(() =>
        {
            var value = 1.0;
            while (!stop.IsCancellationRequested)
                for (var step = 0; step < 100_000; step++)
                    value = Math.Sqrt(value + step) * 1.000001;
            GC.KeepAlive(value);
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }).ToArray();
        foreach (var worker in workers)
            worker.Start();

        var samples = new List<(double Performance, double Limit, double Frequency)>();
        var end = DateTime.UtcNow + duration;
        var warmup = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        try
        {
            while (DateTime.UtcNow < end && !stop.IsCancellationRequested)
            {
                var row = Wmi.Query("SELECT PercentProcessorPerformance, PercentPerformanceLimit, PercentofMaximumFrequency " +
                        "FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'",
                    item => (Convert.ToDouble(item["PercentProcessorPerformance"]), Convert.ToDouble(item["PercentPerformanceLimit"]),
                        Convert.ToDouble(item["PercentofMaximumFrequency"]))).FirstOrDefault();
                if (DateTime.UtcNow >= warmup)
                    samples.Add(row);
                stop.Token.WaitHandle.WaitOne(1000);
            }
        }
        finally
        {
            stop.Cancel();
            foreach (var worker in workers)
                worker.Join();
        }
        if (samples.Count == 0)
            throw new InvalidOperationException("Não foi possível ler os contadores do processador.");
        return new CpuLoadSample((duration - TimeSpan.FromSeconds(3)).TotalSeconds, samples.Average(sample => sample.Performance),
            samples.Min(sample => sample.Limit), samples.Average(sample => sample.Frequency));
    }
}
