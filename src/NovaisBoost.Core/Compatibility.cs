using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

/// <summary>Hardware requirement tokens used in the "compat.hardware" field of a tweak.</summary>
public static partial class Compatibility
{
    public static string? Check(TweakCompatibility compat, HardwareSnapshot pc)
    {
        foreach (var token in compat.Hardware)
        {
            var reason = token switch
            {
                "desktop" => pc.Os.IsLaptop ? "requer um desktop (em notebook afeta bateria e temperatura)" : null,
                "gpu-discrete" => pc.Gpus.Any(gpu => !gpu.IsIntegrated) ? null : "requer uma GPU dedicada",
                "gpu-nvidia" => pc.Gpus.Any(gpu => !gpu.IsIntegrated && gpu.Vendor == GpuVendor.Nvidia) ? null : "requer uma GPU NVIDIA",
                "gpu-hags" => pc.Gpus.Any(SupportsHags) ? null : "requer GPU com HAGS bem suportado (NVIDIA RTX 30 ou mais nova, Radeon RX 6000 ou mais nova, Intel Arc)",
                "not-dual-ccd-x3d" => pc.Cpu is { } cpu && IsDualCcdX3d(cpu.Name)
                    ? "Ryzen X3D com 2 CCDs depende do plano Equilibrado e da Game Bar para colocar o jogo nos núcleos com 3D V-Cache"
                    : null,
                "ram-32gb" => pc.Memory is { TotalBytes: >= MinBytesFor32Gb } ? null : "requer 32 GB de RAM ou mais",
                // Windows Update for Business policies (deferrals) are ignored by the Home edition.
                "not-home" => pc.Os.Caption.Contains("Home", StringComparison.OrdinalIgnoreCase) ? "não funciona no Windows Home (só Pro, Education e Enterprise)" : null,
                _ => $"requisito desconhecido \"{token}\""
            };
            if (reason is not null)
                return reason;
        }
        return null;
    }

    /// <summary>
    /// Null when no installed game uses an anti-cheat listed in "compat.antiCheat"; otherwise the warning to show at review.
    /// </summary>
    public static string? AntiCheatWarning(TweakCompatibility compat, IEnumerable<GameDefinition> installed)
    {
        var games = installed.DistinctBy(game => game.Id)
            .Select(game => (game.Name, AntiCheat: compat.AntiCheat.FirstOrDefault(name => game.AntiCheat.Contains(name, StringComparison.OrdinalIgnoreCase))))
            .Where(game => game.AntiCheat is not null).ToArray();
        if (games.Length == 0)
            return null;
        var list = string.Join(", ", games.Select(game => $"{game.Name} ({game.AntiCheat})"));
        return $"Jogo instalado com anti-cheat que pode recusar este ajuste: {list}. " +
            "Se o jogo pedir, desfaça o ajuste em Reverter / Backups.";
    }

    /// <summary>32 GB modules report a little less to Windows (reserved memory, integrated GPU), so 30 GiB counts as 32 GB.</summary>
    private const long MinBytesFor32Gb = 30L << 30;

    public static bool IsDualCcdX3d(string cpuName) => DualCcdX3d().IsMatch(cpuName);

    /// <summary>
    /// GPUs where HAGS is recommended: RTX 30 and newer, Radeon RX 6000 and newer, Intel Arc. Older cards (GTX, RTX 20,
    /// RX 5000) accept the setting, but it brings no Frame Generation there and gains are not proven.
    /// </summary>
    public static bool SupportsHags(GpuInfo gpu) => !gpu.IsIntegrated && gpu.Vendor switch
    {
        GpuVendor.Nvidia => NvidiaHags().IsMatch(gpu.Name),
        GpuVendor.Amd => AmdHags().IsMatch(gpu.Name),
        GpuVendor.Intel => gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    [GeneratedRegex(@"\b(7900|7950|9900|9950)X3D\b", RegexOptions.IgnoreCase)]
    private static partial Regex DualCcdX3d();

    [GeneratedRegex(@"\bRTX\s?[3-9]0[5-9]0\b", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaHags();

    [GeneratedRegex(@"\bRX\s?[6-9]\d{3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmdHags();
}
