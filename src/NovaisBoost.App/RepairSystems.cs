using System.ComponentModel;
using System.Management;
using System.Security.Principal;
using System.Text.RegularExpressions;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Boot entry of the running Windows ({current}) through bcdedit. Reading and writing need administrator.</summary>
internal sealed partial class WindowsBcdStore : IBcdStore
{
    public BcdState Read(string element)
    {
        string output;
        try
        {
            output = WindowsCommand.Run("bcdedit.exe", "/enum", "{current}");
        }
        catch (Win32Exception) when (!IsElevated())
        {
            throw new UnauthorizedAccessException("Ler a configuração de inicialização (BCD) precisa de administrador.");
        }
        // Element names are not translated by bcdedit; boolean values are ("Yes" / "Sim" / "Ja"...).
        foreach (var line in output.Split('\n'))
        {
            var match = ElementLine().Match(line.TrimEnd());
            if (match.Success && string.Equals(match.Groups[1].Value, element, StringComparison.OrdinalIgnoreCase))
                return new BcdState(Canonical(match.Groups[2].Value.Trim()));
        }
        return new BcdState(null);
    }

    public void Write(string element, BcdState state)
    {
        if (state.Value is null)
            WindowsCommand.Run("bcdedit.exe", "/deletevalue", "{current}", element);
        else
            WindowsCommand.Run("bcdedit.exe", "/set", "{current}", element, state.Value);
    }

    internal static string Canonical(string value) => value.ToLowerInvariant() switch
    {
        "yes" or "sim" or "ja" or "oui" or "sí" or "si" or "true" or "on" => "yes",
        "no" or "não" or "nao" or "nein" or "non" or "false" or "off" => "no",
        var other => other
    };

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [GeneratedRegex(@"^([a-z][a-z0-9]+)\s{2,}(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ElementLine();
}

/// <summary>Paging file through WMI: Win32_ComputerSystem.AutomaticManagedPagefile and Win32_PageFileSetting.</summary>
internal sealed class WindowsPagefile : IPagefile
{
    public PagefileState Read()
    {
        var automatic = Wmi.Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem", item => item["AutomaticManagedPagefile"] is true).FirstOrDefault();
        var files = Wmi.Query("SELECT Name, InitialSize, MaximumSize FROM Win32_PageFileSetting", item => new PagefileSetting(
            ((string?)item["Name"] ?? "?").Trim(), Convert.ToUInt32(item["InitialSize"] ?? 0u), Convert.ToUInt32(item["MaximumSize"] ?? 0u)));
        return new PagefileState(automatic, automatic ? [] : files);
    }

    public void Write(PagefileState state)
    {
        var scope = new ManagementScope(@"root\cimv2", new ConnectionOptions { EnablePrivileges = true });
        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem")))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject computer in results)
            {
                using (computer)
                {
                    computer["AutomaticManagedPagefile"] = state.SystemManaged;
                    computer.Put();
                }
            }
        }
        if (state.SystemManaged)
            return;

        using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_PageFileSetting")))
        using (var results = searcher.Get())
        {
            foreach (ManagementObject setting in results)
            {
                using (setting)
                    setting.Delete();
            }
        }
        using var type = new ManagementClass(scope, new ManagementPath("Win32_PageFileSetting"), null);
        foreach (var file in state.Files)
        {
            using var instance = type.CreateInstance();
            instance["Name"] = file.Path;
            instance["InitialSize"] = file.InitialMb;
            instance["MaximumSize"] = file.MaximumMb;
            instance.Put();
        }
    }
}

/// <summary>
/// Global TCP settings. Read through WMI (independent of the Windows language: MSFT_NetTCPSetting "Internet" and
/// MSFT_NetOffloadGlobalSetting), written with "netsh int tcp set global".
/// </summary>
internal sealed class WindowsTcpSettings : ITcpSettings
{
    private const string Scope = @"root\StandardCimv2";
    private static readonly string[] AutoTuningLevels = ["disabled", "highlyrestricted", "restricted", "normal", "experimental"];

    public TcpState Read(string setting) => setting.ToLowerInvariant() switch
    {
        "autotuninglevel" => new TcpState(Wmi.Query("SELECT AutoTuningLevelLocal FROM MSFT_NetTCPSetting WHERE SettingName = 'Internet'",
                item => Convert.ToInt32(item["AutoTuningLevelLocal"] ?? -1), Scope)
            .Select(level => level >= 0 && level < AutoTuningLevels.Length ? AutoTuningLevels[level] : "desconhecido").FirstOrDefault() ?? "desconhecido"),
        "rss" => new TcpState(Wmi.Query("SELECT ReceiveSideScaling FROM MSFT_NetOffloadGlobalSetting",
                item => Convert.ToInt32(item["ReceiveSideScaling"] ?? -1), Scope)
            .Select(value => value switch { 1 => "enabled", 0 => "disabled", _ => "desconhecido" }).FirstOrDefault() ?? "desconhecido"),
        _ => throw new InvalidDataException($"Configuração TCP desconhecida: {setting}.")
    };

    public void Write(string setting, TcpState state)
    {
        if (state.Value == "desconhecido")
            throw new InvalidOperationException($"O valor anterior de {setting} não pôde ser lido; não há o que restaurar.");
        WindowsCommand.Run("netsh.exe", "int", "tcp", "set", "global", $"{setting.ToLowerInvariant()}={state.Value.ToLowerInvariant()}");
    }
}
