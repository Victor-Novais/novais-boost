using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>F19 collection: disk reliability, network adapter tuning, interrupt affinity, USB tree, BypassIO. Read-only.</summary>
internal sealed partial class WindowsHardwareProbe
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private static IReadOnlyList<DiskReliability> ReadReliability()
    {
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, ObjectId FROM MSFT_PhysicalDisk",
            item => (Id: Convert.ToString(item["DeviceId"]) ?? "", Name: (Convert.ToString(item["FriendlyName"]) ?? "?").Trim(),
                ObjectId: Convert.ToString(item["ObjectId"]) ?? ""), StorageScope);
        var counters = new List<DiskReliability>();
        foreach (var disk in disks.DistinctBy(disk => disk.Id))
        {
            var escaped = disk.ObjectId.Replace(@"\", @"\\").Replace("\"", "\\\"");
            var row = Wmi.Query($"ASSOCIATORS OF {{MSFT_PhysicalDisk.ObjectId=\"{escaped}\"}} WHERE AssocClass = MSFT_PhysicalDiskToStorageReliabilityCounter",
                item => (Temperature: Int(item["Temperature"]), Max: Int(item["TemperatureMax"]), Wear: Int(item["Wear"]),
                    Errors: Long(item["ReadErrorsUncorrected"]), Hours: Long(item["PowerOnHours"])), StorageScope).FirstOrDefault();
            counters.Add(new DiskReliability(disk.Name, Positive(row.Temperature), Positive(row.Max), row.Wear, row.Errors, row.Hours,
                int.TryParse(disk.Id, out var number) ? WriteCache(number) : null));
        }
        return counters;

        static int? Int(object? value) => value is null ? null : Convert.ToInt32(value);
        static long? Long(object? value) => value is null ? null : Convert.ToInt64(value);
        static int? Positive(int? value) => value is > 0 ? value : null;
    }

    /// <summary>IOCTL_DISK_GET_CACHE_INFORMATION: DISK_CACHE_INFORMATION.WriteCacheEnabled (byte 2). Needs administrator.</summary>
    private static bool? WriteCache(int diskNumber)
    {
        using var handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}", 0x80000000 /* GENERIC_READ */, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;
        var buffer = new byte[24];
        return DeviceIoControl(handle, 0x000740D4, IntPtr.Zero, 0, buffer, buffer.Length, out var returned, IntPtr.Zero) && returned >= 3
            ? buffer[2] != 0
            : null;
    }

    private static IReadOnlyList<NicTuning> ReadNics()
    {
        using var root = Registry.LocalMachine.OpenSubKey(NetClassKey);
        var result = new List<NicTuning>();
        foreach (var name in root?.GetSubKeyNames().Where(name => Regex.IsMatch(name, @"^\d{4}$")) ?? [])
        {
            using var key = root!.OpenSubKey(name);
            // NCF_PHYSICAL (0x4) and Ethernet/Wi-Fi interface types only.
            if (key is null || (Convert.ToInt32(key.GetValue("Characteristics") ?? 0) & 0x4) == 0 ||
                key.GetValue("*IfType") is not (int or string) || Convert.ToInt32(key.GetValue("*IfType")) is not (6 or 71))
                continue;
            var rss = key.GetValue("*RSS") as string;
            var buffers = key.GetValue("*ReceiveBuffers") as string;
            result.Add(new NicTuning((key.GetValue("DriverDesc") as string ?? "?").Trim(),
                rss is null ? null : rss != "0", int.TryParse(buffers, out var count) ? count : null));
        }
        return result;
    }

    private static IReadOnlyList<InterruptAffinity> ReadAffinity()
    {
        var devices = Wmi.Query("SELECT PNPDeviceID, Name, PNPClass FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\\\%'",
            item => (Id: item["PNPDeviceID"] as string, Name: (item["Name"] as string ?? "?").Trim(), Class: item["PNPClass"] as string));
        var result = new List<InterruptAffinity>();
        foreach (var (id, name, _) in devices.Where(device => device.Id is not null && device.Class is "Display" or "Net" or "USB" or "SCSIAdapter" or "HDC"))
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}\Device Parameters\Interrupt Management\Affinity Policy");
            if (key is null)
                continue;
            var policy = Convert.ToInt32(key.GetValue("DevicePolicy") ?? 0);
            ulong? mask = key.GetValue("AssignmentSetOverride") switch
            {
                byte[] bytes when bytes.Length > 0 => BitConverter.ToUInt64([.. bytes.Take(8), .. new byte[Math.Max(0, 8 - bytes.Length)]]),
                long number => (ulong)number,
                int number => (uint)number,
                _ => null
            };
            if (policy != 0 || mask is not null)
                result.Add(new InterruptAffinity(name, policy, mask));
        }
        return result;
    }

    private static IReadOnlyList<UsbDevice> ReadUsb()
    {
        var controllers = Wmi.Query("SELECT PNPDeviceID, Name FROM Win32_PnPEntity WHERE PNPClass = 'USB' AND PNPDeviceID LIKE 'PCI\\\\%'",
                item => (Id: item["PNPDeviceID"] as string, Name: (item["Name"] as string ?? "?").Trim()))
            .Where(item => item.Id is not null).ToDictionary(item => item.Id!, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var devices = Wmi.Query("SELECT PNPDeviceID, Name, PNPClass FROM Win32_PnPEntity WHERE PNPClass = 'Mouse' OR PNPClass = 'Keyboard' OR " +
            "PNPClass = 'Camera' OR PNPClass = 'Image' OR PNPClass = 'MEDIA'",
            item => (Id: item["PNPDeviceID"] as string, Name: (item["Name"] as string ?? "?").Trim(), Class: item["PNPClass"] as string ?? ""));
        var result = new List<UsbDevice>();
        foreach (var (id, name, deviceClass) in devices.Where(device => device.Id is not null))
        {
            if (ControllerOf(id!) is not { } controller || !controllers.TryGetValue(controller, out var controllerName))
                continue;
            var kind = deviceClass switch
            {
                "Mouse" => UsbDeviceKind.Mouse,
                "Keyboard" => UsbDeviceKind.Keyboard,
                "Camera" or "Image" => UsbDeviceKind.Camera,
                _ when Regex.IsMatch(name, "capture|cam link|elgato|avermedia|hd60|live gamer", RegexOptions.IgnoreCase) => UsbDeviceKind.Capture,
                _ => UsbDeviceKind.Audio
            };
            result.Add(new UsbDevice(name, kind, controllerName));
        }
        return result.DistinctBy(device => (device.Name, device.Controller)).ToArray();
    }

    /// <summary>Walks up the device tree from a USB/HID device to its PCI host controller.</summary>
    private static string? ControllerOf(string instanceId)
    {
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0)
            return null;
        var sawUsb = instanceId.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase);
        for (var depth = 0; depth < 16 && CM_Get_Parent(out var parent, node, 0) == 0; depth++)
        {
            node = parent;
            if (DeviceId(node) is not { } id)
                return null;
            if (id.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase))
                sawUsb = true;
            if (id.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase))
                return sawUsb ? id : null;
        }
        return null;
    }

    private static string? ReadBypassIo()
    {
        var output = WindowsCommand.Run("fsutil.exe", "bypassio", "state", (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\'));
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, [Out] byte[] output, int outputSize,
        out int returned, IntPtr overlapped);
}

/// <summary>F19: DirectXUserGlobalSettings entries (Windows graphics settings) of the current user.</summary>
internal sealed class WindowsDirectXSettings(string? userSid = null) : IDirectXSettings
{
    private RegistryKey Root() => userSid is null ? Registry.CurrentUser
        : Registry.Users.OpenSubKey(userSid, writable: true) ?? throw new InvalidOperationException("O perfil do usuário não está carregado.");

    public string? Read(string key)
    {
        using var registry = Root().OpenSubKey(DirectXSettings.Path);
        return DirectXSettings.Get(registry?.GetValue(DirectXSettings.ValueName) as string, key);
    }

    public void Write(string key, string? value)
    {
        using var registry = Root().CreateSubKey(DirectXSettings.Path);
        var updated = DirectXSettings.Set(registry.GetValue(DirectXSettings.ValueName) as string, key, value);
        if (updated is null)
            registry.DeleteValue(DirectXSettings.ValueName, throwOnMissingValue: false);
        else
            registry.SetValue(DirectXSettings.ValueName, updated, RegistryValueKind.String);
    }
}

/// <summary>F19: devices by instance id (status through cfgmgr32, change through pnputil, which persists across reboots).</summary>
internal sealed class WindowsDevices : IDevices
{
    private const uint ProblemDisabled = 22; // CM_PROB_DISABLED

    public bool? IsEnabled(string instanceId)
    {
        if (CM_Locate_DevNodeW(out var node, instanceId, 0) != 0)
            return null;
        return CM_Get_DevNode_Status(out _, out var problem, node, 0) != 0 || problem != ProblemDisabled;
    }

    public void SetEnabled(string instanceId, bool enabled)
    {
        try
        {
            WindowsCommand.Run(TimeSpan.FromSeconds(60), "pnputil.exe", enabled ? "/enable-device" : "/disable-device", instanceId);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 3010)
        {
            // ERROR_SUCCESS_REBOOT_REQUIRED: done, takes full effect after a restart.
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
}
