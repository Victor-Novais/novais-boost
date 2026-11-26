using System.Text.RegularExpressions;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// Tweaks generated from this PC's real devices and games (paths differ on every machine).
/// They go through the same engine, review and backup as the catalog tweaks.
/// </summary>
internal sealed class DeviceTweaks(IActionLog log)
{
    private const string NetClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private static readonly TweakCompatibility None = new([], []);

    /// <summary>Network adapter keywords that save power at the cost of latency, and the value that turns them off.</summary>
    private static readonly (string Keyword, string Off)[] NicPowerKeywords =
    [
        ("*EEE", "0"), ("EEELinkAdvertisement", "0"), ("AdvancedEEE", "0"), ("EnableGreenEthernet", "0"), ("GigaLite", "0"),
        ("PowerSavingMode", "0"), ("AutoPowerSaveModeEnabled", "0"), ("*SelectiveSuspend", "0"), ("ULPMode", "0"),
        ("EnableConnectedPowerGating", "0"), ("EnableDynamicPowerGating", "0"), ("EnablePowerManagement", "0")
    ];

    public IReadOnlyList<TweakDefinition> Generate(IReadOnlyList<InstalledGame> games, HardwareSnapshot pc)
    {
        var tweaks = new List<TweakDefinition>();
        void Add(string name, Func<TweakDefinition?> build)
        {
            try
            {
                if (build() is { } tweak)
                    tweaks.Add(tweak);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                log.Warning($"Ajuste dinâmico '{name}' indisponível: {error.Message}");
            }
        }

        Add("msi-mode", MsiMode);
        Add("nic-power", () => NicKeywords("nic-power", "Placa de rede sem economia de energia",
            "Desliga Energy Efficient Ethernet, Green Ethernet e economias de energia da placa de rede, que adicionam atraso ao acordar o link.",
            TweakLevel.Competitive, NicPowerKeywords, includePnpCapabilities: true));
        Add("nic-interrupt-moderation", () => NicKeywords("nic-interrupt-moderation", "Placa de rede sem moderação de interrupções",
            "Cada pacote gera interrupção na hora, sem agrupar. Menos latência de rede, mais uso de CPU.",
            TweakLevel.Extreme, [("*InterruptModeration", "0")], includePnpCapabilities: false));
        var playable = games.Where(game => game.Game.Id != "obs").ToArray();
        Add("games-gpu-preference", () => GpuPreference(games, pc));
        Add("games-qos", () => Qos(playable));
        return tweaks;
    }

    private TweakDefinition? MsiMode()
    {
        var devices = Wmi.Query("SELECT PNPDeviceID, Name, PNPClass FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'PCI\\\\%'",
            item => (Id: item["PNPDeviceID"] as string, Name: (item["Name"] as string ?? "?").Trim(), Class: item["PNPClass"] as string));
        var actions = new List<TweakAction>();
        var names = new List<string>();
        foreach (var (id, name, deviceClass) in devices)
        {
            if (id is null || deviceClass is not ("Display" or "SCSIAdapter" or "HDC" or "Net" or "USB"))
                continue;
            var path = $@"SYSTEM\CurrentControlSet\Enum\{id}\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties";
            using var key = Registry.LocalMachine.OpenSubKey(path);
            // Only devices whose driver declares MSI support (the key exists) and that are not already in MSI mode.
            if (key is null || key.GetValue("MSISupported") is 1)
                continue;
            actions.Add(Dword(RegistryHiveName.LocalMachine, path, "MSISupported", 1));
            names.Add(name);
        }
        return actions.Count == 0 ? null : new TweakDefinition("msi-mode", "Interrupções em modo MSI",
            $"Coloca em modo MSI (Message Signaled Interrupts) os dispositivos que suportam e ainda não usam: {string.Join(", ", names)}. " +
            "Interrupções MSI têm menos latência e não compartilham linha com outros dispositivos.",
            "Latência", TweakLevel.Competitive, "Baixo. Se algum dispositivo falhar após reiniciar, use Reverter tudo.", true, None, actions.ToArray());
    }

    private TweakDefinition? NicKeywords(string id, string name, string description, TweakLevel level,
        (string Keyword, string Off)[] keywords, bool includePnpCapabilities)
    {
        using var root = Registry.LocalMachine.OpenSubKey(NetClass);
        if (root is null)
            return null;
        var actions = new List<TweakAction>();
        var adapters = new List<string>();
        foreach (var sub in root.GetSubKeyNames().Where(sub => Regex.IsMatch(sub, @"^\d{4}$")))
        {
            RegistryKey? adapter;
            try
            {
                adapter = root.OpenSubKey(sub);
            }
            catch (System.Security.SecurityException)
            {
                continue;
            }
            using (adapter)
            {
                // NCF_PHYSICAL (0x4): skip virtual adapters (VPN, Hyper-V, WSL).
                if (adapter?.GetValue("Characteristics") is not int characteristics || (characteristics & 0x4) == 0)
                    continue;
                var path = $@"{NetClass}\{sub}";
                var before = actions.Count;
                foreach (var (keyword, off) in keywords)
                    if (adapter.GetValue(keyword) is string current && current != off)
                        actions.Add(new RegistryAction(RegistryHiveName.LocalMachine, path, keyword, RegistryOperation.Set,
                            RegistryValue.From(RegistryValueType.String, off)));
                if (includePnpCapabilities && adapter.GetValue("PnPCapabilities") is not 24)
                    actions.Add(Dword(RegistryHiveName.LocalMachine, path, "PnPCapabilities", 24));
                if (actions.Count > before)
                    adapters.Add(adapter.GetValue("DriverDesc") as string ?? sub);
            }
        }
        return actions.Count == 0 ? null : new TweakDefinition(id, name, $"{description} Adaptadores: {string.Join(", ", adapters)}.",
            "Rede", level, "Baixo. O adaptador é reiniciado no próximo boot.", true,
            level == TweakLevel.Competitive ? new TweakCompatibility(["desktop"], []) : None, actions.ToArray());
    }

    private static TweakDefinition? GpuPreference(IReadOnlyList<InstalledGame> games, HardwareSnapshot pc)
    {
        if (games.Count == 0 || !pc.Gpus.Any(gpu => !gpu.IsIntegrated))
            return null;
        var actions = games.Select(game => (TweakAction)new RegistryAction(RegistryHiveName.CurrentUser,
                @"Software\Microsoft\DirectX\UserGpuPreferences", game.ExecutablePath, RegistryOperation.Set,
                RegistryValue.From(RegistryValueType.String, "GpuPreference=2;")))
            .DistinctBy(action => ((RegistryAction)action).ValueName, StringComparer.OrdinalIgnoreCase).ToArray();
        return new TweakDefinition("games-gpu-preference", "Jogos e OBS sempre na GPU dedicada",
            $"Fixa {string.Join(", ", games.Select(game => game.Game.Name).Distinct())} na GPU de alto desempenho " +
            "(Configurações > Sistema > Tela > Gráficos). Evita o jogo rodar na GPU integrada.",
            "Jogos", TweakLevel.Safe, "Nenhum.", false, new TweakCompatibility(["gpu-discrete"], []), actions);
    }

    private static TweakDefinition? Qos(IReadOnlyList<InstalledGame> games)
    {
        if (games.Count == 0)
            return null;
        var actions = new List<TweakAction>
        {
            new RegistryAction(RegistryHiveName.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\QoS", "Do not use NLA",
                RegistryOperation.Set, RegistryValue.From(RegistryValueType.String, "1"))
        };
        foreach (var exe in games.Select(game => Path.GetFileName(game.ExecutablePath)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = $@"SOFTWARE\Policies\Microsoft\Windows\QoS\NOVAIS boost {Path.GetFileNameWithoutExtension(exe)}";
            foreach (var (name, value) in new[]
            {
                ("Version", "1.0"), ("Application Name", exe), ("Protocol", "*"), ("Local Port", "*"), ("Local IP", "*"),
                ("Local IP Prefix Length", "*"), ("Remote Port", "*"), ("Remote IP", "*"), ("Remote IP Prefix Length", "*"),
                ("DSCP Value", "46"), ("Throttle Rate", "-1")
            })
                actions.Add(new RegistryAction(RegistryHiveName.LocalMachine, key, name, RegistryOperation.Set, RegistryValue.From(RegistryValueType.String, value)));
        }
        return new TweakDefinition("games-qos", "Prioridade de rede (QoS) para os jogos",
            "Marca os pacotes dos jogos com DSCP 46 (tráfego em tempo real). Roteadores com QoS/WMM priorizam esses pacotes.",
            "Rede", TweakLevel.Competitive, "Nenhum. Roteadores sem QoS ignoram a marcação.", true, None, actions.ToArray());
    }

    private static RegistryAction Dword(RegistryHiveName hive, string path, string name, int value) =>
        new(hive, path, name, RegistryOperation.Set, RegistryValue.From(RegistryValueType.Dword, value));
}
