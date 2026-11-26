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
                "gpu-hags" => pc.Gpus.Any(SupportsHags) ? null : "requer GPU com suporte a HAGS (NVIDIA GTX 10/RTX, Radeon RX 5000+ ou Intel Arc)",
                "not-dual-ccd-x3d" => pc.Cpu is { } cpu && IsDualCcdX3d(cpu.Name)
                    ? "Ryzen X3D com 2 CCDs depende do plano Equilibrado e da Game Bar para colocar o jogo nos núcleos com 3D V-Cache"
                    : null,
                _ => $"requisito desconhecido \"{token}\""
            };
            if (reason is not null)
                return reason;
        }
        return null;
    }

    public static bool IsDualCcdX3d(string cpuName) => DualCcdX3d().IsMatch(cpuName);

    public static bool SupportsHags(GpuInfo gpu) => !gpu.IsIntegrated && gpu.Vendor switch
    {
        GpuVendor.Nvidia => NvidiaHags().IsMatch(gpu.Name),
        GpuVendor.Amd => AmdHags().IsMatch(gpu.Name),
        GpuVendor.Intel => gpu.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    [GeneratedRegex(@"\b(7900|7950|9900|9950)X3D\b", RegexOptions.IgnoreCase)]
    private static partial Regex DualCcdX3d();

    [GeneratedRegex(@"\bRTX\b|\bGTX\s?(10[5-8]0|16[5-6]0)", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaHags();

    [GeneratedRegex(@"\bRX\s?[5-9]\d{3}\b", RegexOptions.IgnoreCase)]
    private static partial Regex AmdHags();
}
