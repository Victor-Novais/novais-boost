using System.Runtime.InteropServices;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// F19: pauses the Wi-Fi background scan (which adds latency spikes every minute or so) while a game runs, through
/// WlanSetInterface(wlan_intf_opcode_background_scan_enabled). The setting does not survive a reboot, and Resume turns it
/// back on when the game closes.
/// </summary>
internal static partial class WifiScan
{
    private const int BackgroundScanEnabled = 2; // WLAN_INTF_OPCODE.wlan_intf_opcode_background_scan_enabled
    private static readonly List<Guid> Paused = [];

    /// <summary>Returns how many connected Wi-Fi interfaces were paused.</summary>
    public static int Pause(IActionLog log) => Set(false, Interfaces(connectedOnly: true), log);

    public static void Resume(IActionLog log)
    {
        if (Paused.Count == 0)
            return;
        Set(true, Paused.ToArray(), log);
        Paused.Clear();
    }

    private static int Set(bool enabled, IReadOnlyList<Guid> interfaces, IActionLog log)
    {
        if (interfaces.Count == 0 || WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0)
            return 0;
        var changed = 0;
        try
        {
            var value = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(value, enabled ? 1 : 0);
                foreach (var id in interfaces)
                {
                    var guid = id;
                    var result = WlanSetInterface(handle, ref guid, BackgroundScanEnabled, sizeof(int), value, IntPtr.Zero);
                    if (result != 0)
                    {
                        log.Warning($"Guardian: não foi possível {(enabled ? "religar" : "pausar")} a varredura Wi-Fi (erro {result}).");
                        continue;
                    }
                    changed++;
                    if (!enabled)
                        Paused.Add(id);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(value);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
        if (changed > 0)
            log.Info($"Guardian: varredura Wi-Fi em segundo plano {(enabled ? "religada" : "pausada")} em {changed} adaptador(es).");
        return changed;
    }

    private static Guid[] Interfaces(bool connectedOnly)
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var handle) != 0)
            return [];
        try
        {
            if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0)
                return [];
            try
            {
                // WLAN_INTERFACE_INFO_LIST: DWORD count, DWORD index, then WLAN_INTERFACE_INFO[] (GUID 16 + WCHAR[256] + state DWORD).
                var count = Marshal.ReadInt32(list);
                const int itemSize = 16 + 512 + 4;
                var result = new List<Guid>();
                for (var index = 0; index < count; index++)
                {
                    var item = list + 8 + index * itemSize;
                    var bytes = new byte[16];
                    Marshal.Copy(item, bytes, 0, 16);
                    var state = Marshal.ReadInt32(item + 16 + 512);
                    if (!connectedOnly || state == 1) // wlan_interface_state_connected
                        result.Add(new Guid(bytes));
                }
                return result.ToArray();
            }
            finally
            {
                WlanFreeMemory(list);
            }
        }
        finally
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanOpenHandle(int clientVersion, IntPtr reserved, out int negotiatedVersion, out IntPtr handle);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanCloseHandle(IntPtr handle, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanSetInterface(IntPtr handle, ref Guid interfaceGuid, int opCode, int dataSize, IntPtr data, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(IntPtr memory);
}
