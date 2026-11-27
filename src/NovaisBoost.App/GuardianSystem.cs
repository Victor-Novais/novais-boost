using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>What the Guardian reads about this PC once: CPU topology and whether it is a dual-CCD X3D.</summary>
internal sealed class GuardianEnvironment
{
    public GuardianEnvironment()
    {
        Topology = CpuTopologyReader.TryRead();
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        DualCcdX3d = key?.GetValue("ProcessorNameString") is string cpu && Compatibility.IsDualCcdX3d(cpu);
    }

    public CpuTopology? Topology { get; }
    public bool DualCcdX3d { get; }

    public (long FreeBytes, long StandbyBytes, double UsedPercent)? ReadMemory()
    {
        try
        {
            var row = Wmi.Query("SELECT FreeAndZeroPageListBytes, StandbyCacheNormalPriorityBytes, StandbyCacheReserveBytes, StandbyCacheCoreBytes, AvailableBytes " +
                "FROM Win32_PerfFormattedData_PerfOS_Memory", item => (
                    Free: Convert.ToInt64(item["FreeAndZeroPageListBytes"]),
                    Standby: Convert.ToInt64(item["StandbyCacheNormalPriorityBytes"]) + Convert.ToInt64(item["StandbyCacheReserveBytes"]) +
                        Convert.ToInt64(item["StandbyCacheCoreBytes"]),
                    Available: Convert.ToInt64(item["AvailableBytes"]))).Single();
            var total = Wmi.Query("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem", item => Convert.ToInt64(item["TotalVisibleMemorySize"]) * 1024).Single();
            return (row.Free, row.Standby, total <= 0 ? 0 : 100d * (total - row.Available) / total);
        }
        catch (Exception error) when (error is ManagementException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    /// <summary>Highest VRAM use across NVIDIA GPUs (NVML); null without an NVIDIA driver.</summary>
    public double? ReadVramPercent() => Nvml.ReadMemory().Values.Select(memory => (double?)memory.UsedPercent).Max();
}

/// <summary>Wakes the idle Guardian when any process starts (Win32_ProcessStartTrace, needs administrator).</summary>
internal sealed class ProcessStartEvents : IDisposable
{
    private readonly ManagementEventWatcher _watcher;

    private ProcessStartEvents(ManagementEventWatcher watcher)
    {
        _watcher = watcher;
        _watcher.EventArrived += (_, args) =>
        {
            args.NewEvent.Dispose();
            Signal.Set();
        };
        _watcher.Start();
    }

    public ManualResetEvent Signal { get; } = new(false);

    public static ProcessStartEvents? TryCreate(IActionLog log)
    {
        try
        {
            return new ProcessStartEvents(new ManagementEventWatcher(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStartTrace")));
        }
        catch (Exception error) when (error is ManagementException or UnauthorizedAccessException or COMException)
        {
            log.Warning($"Guardian: eventos de processo indisponíveis ({error.Message}); usando verificação periódica.");
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _watcher.Stop();
        }
        catch (ManagementException)
        {
            // Already stopped.
        }
        _watcher.Dispose();
        Signal.Dispose();
    }
}

/// <summary>Per-process controls used by the Guardian. Every change is undone by the same functions.</summary>
internal static class ProcessControl
{
    private const uint ProcessSetInformation = 0x0200, ProcessSetLimitedInformation = 0x2000, ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessPowerThrottling = 4;
    private const uint ThrottleExecutionSpeed = 0x1;

    /// <summary>17.2: Windows efficiency mode (EcoQoS). false = back to "let Windows decide".</summary>
    public static bool SetEfficiencyMode(int processId, bool enabled)
    {
        using var handle = OpenProcess(ProcessSetInformation, false, processId);
        if (handle.IsInvalid)
            return false;
        var state = new PowerThrottlingState { Version = 1, ControlMask = enabled ? ThrottleExecutionSpeed : 0, StateMask = enabled ? ThrottleExecutionSpeed : 0 };
        return SetProcessInformation(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf<PowerThrottlingState>());
    }

    /// <summary>17.3/17.4: default CPU sets of a process; an empty list removes the restriction.</summary>
    public static bool SetCpuSets(int processId, IReadOnlyList<uint> cpuSets)
    {
        using var handle = OpenProcess(ProcessSetLimitedInformation | ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
            return false;
        return cpuSets.Count == 0 ? SetProcessDefaultCpuSets(handle, null, 0) : SetProcessDefaultCpuSets(handle, cpuSets.ToArray(), (uint)cpuSets.Count);
    }

    /// <summary>17.6: NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList).</summary>
    public static bool PurgeStandbyList()
    {
        if (!Privileges.Enable("SeProfileSingleProcessPrivilege"))
            return false;
        var command = 4; // MemoryPurgeStandbyList
        return NtSetSystemInformation(80, ref command, sizeof(int)) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version, ControlMask, StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetProcessInformation(Microsoft.Win32.SafeHandles.SafeProcessHandle process, int informationClass,
        ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool SetProcessDefaultCpuSets(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint[]? cpuSetIds, uint count);

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtSetSystemInformation(int informationClass, ref int information, int length);
}

/// <summary>Enables a privilege the elevated token already has (disabled by default).</summary>
internal static class Privileges
{
    public static bool Enable(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out var token)) // ADJUST_PRIVILEGES | QUERY
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid))
                return false;
            var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 }; // SE_PRIVILEGE_ENABLED
            return AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Reads CPU sets (efficiency class, L3 index) and the L3 size of each cache, for E-core / V-Cache CCD placement.</summary>
internal static class CpuTopologyReader
{
    public static CpuTopology? TryRead()
    {
        try
        {
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out var length, IntPtr.Zero, 0);
            if (length == 0)
                return null;
            var buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0))
                    return null;
                var sets = new List<CpuSetInfo>();
                for (var offset = 0; offset < length;)
                {
                    var size = Marshal.ReadInt32(buffer, offset);
                    // SYSTEM_CPU_SET_INFORMATION.CpuSet: Id at +8, LastLevelCacheIndex at +16, EfficiencyClass at +18.
                    sets.Add(new CpuSetInfo((uint)Marshal.ReadInt32(buffer, offset + 8), Marshal.ReadByte(buffer, offset + 18), Marshal.ReadByte(buffer, offset + 16)));
                    offset += size;
                }
                return new CpuTopology(sets, L3Sizes(sets));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>L3 sizes in LLC order (GetLogicalProcessorInformationEx, RelationCache), matched to the LLC indices in use.</summary>
    private static Dictionary<byte, long> L3Sizes(List<CpuSetInfo> sets)
    {
        const int relationCache = 2;
        GetLogicalProcessorInformationEx(relationCache, IntPtr.Zero, out var length);
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(relationCache, buffer, out length))
                return [];
            var sizes = new List<long>();
            for (var offset = 0; offset < length;)
            {
                var size = Marshal.ReadInt32(buffer, offset + 4);
                // CACHE_RELATIONSHIP at +8: Level (byte), Associativity (byte), LineSize (word), CacheSize (dword), Type (dword).
                if (Marshal.ReadByte(buffer, offset + 8) == 3)
                    sizes.Add((uint)Marshal.ReadInt32(buffer, offset + 12));
                offset += size;
            }
            var indices = sets.Select(set => set.LastLevelCacheIndex).Distinct().Order().ToArray();
            return indices.Length == sizes.Count ? indices.Zip(sizes).ToDictionary(pair => pair.First, pair => pair.Second) : [];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, uint length, out uint returnedLength, IntPtr process, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, out uint length);
}

/// <summary>Active power plan (powercfg) for the "plan only during the game" option.</summary>
internal static partial class PowerPlans
{
    public static readonly Guid Novais = new("4e6f7661-6973-426f-6f73-740000000001");

    public static Guid? Active() => GuidPattern().Match(WindowsCommand.Run("powercfg.exe", "/getactivescheme")) is { Success: true } match
        ? Guid.Parse(match.Value) : null;

    public static bool NovaisPlanInstalled() => WindowsCommand.Run("powercfg.exe", "/list").Contains(Novais.ToString("D"), StringComparison.OrdinalIgnoreCase);

    public static bool Activate(Guid plan)
    {
        try
        {
            WindowsCommand.Run("powercfg.exe", "/setactive", plan.ToString("D"));
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}

/// <summary>17.7: Defender folder exclusions through MSFT_MpPreference (needs administrator; Tamper Protection may block).</summary>
internal sealed class WindowsDefenderExclusions : IDefenderExclusions
{
    private const string Scope = @"root\Microsoft\Windows\Defender";

    public bool Contains(string path) => Wmi.Query("SELECT ExclusionPath FROM MSFT_MpPreference", item => item["ExclusionPath"] as string[] ?? [], Scope)
        .SelectMany(paths => paths).Any(existing => string.Equals(existing.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    public void Set(string path, bool excluded)
    {
        using var preference = new ManagementClass(Scope, "MSFT_MpPreference", null);
        using var parameters = preference.GetMethodParameters(excluded ? "Add" : "Remove");
        parameters["ExclusionPath"] = new[] { path };
        using var result = preference.InvokeMethod(excluded ? "Add" : "Remove", parameters, null);
        var code = Convert.ToUInt32(result?["ReturnValue"] ?? 0u);
        if (code != 0)
            throw new InvalidOperationException($"O Defender recusou a exclusão (código 0x{code:X8}). Se a Proteção contra Adulteração " +
                "estiver ligada, adicione a exclusão em Segurança do Windows > Proteção contra vírus e ameaças > Exclusões.");
    }
}
