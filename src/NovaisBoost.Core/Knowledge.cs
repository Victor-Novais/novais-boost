using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

/// <summary>Embedded JSON knowledge bases under data/*.json.</summary>
public static class DataCatalog
{
    public static IReadOnlyList<HardwareProfile> Profiles() => Load<HardwareProfile[]>("profiles.json");
    public static IReadOnlyList<ProcessEntry> Processes() => Load<ProcessEntry[]>("processes.json");
    public static IReadOnlyList<GameDefinition> Games() => Load<GameDefinition[]>("games.json");
    public static IReadOnlyList<MicrocodeRequirement> Microcode() => Load<MicrocodeRequirement[]>("microcode.json");
    public static IReadOnlyList<MemorySlotPattern> MemorySlots() => Load<MemorySlotPattern[]>("memory-slots.json");
    public static IReadOnlyList<OptimizerDefinition> Optimizers() => Load<OptimizerDefinition[]>("optimizers.json");

    private static T Load<T>(string name)
    {
        using var stream = typeof(DataCatalog).Assembly.GetManifestResourceStream("Data." + name)
            ?? throw new InvalidDataException($"Base de dados ausente: {name}.");
        try
        {
            return JsonSerializer.Deserialize<T>(stream, TweakCatalog.JsonOptions) ?? throw new InvalidDataException($"{name} está vazio.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Base de dados inválida em {name} (linha {(error.LineNumber ?? 0) + 1}, campo {error.Path}).", error);
        }
    }
}

// ---------------------------------------------------------------- Hardware profiles (F4)

/// <summary>Every non-null field must match. Regexes are case-insensitive.</summary>
public sealed record ProfileMatch(
    string? Cpu = null, string? Gpu = null, string? Board = null, MemoryKind? Memory = null,
    int? MinRamGb = null, int? MaxRamGb = null, bool? Laptop = null);

public sealed record ProfileAdvice(string Title, string Detail);

/// <param name="Tweaks">Tweak ids recommended for this hardware (applied through the normal review flow).</param>
/// <param name="Avoid">Tweak ids that must not be applied on this hardware.</param>
public sealed record HardwareProfile(
    string Id, string Name, string Component, ProfileMatch Match,
    ProfileAdvice[] Recommendations, string[] Bios, string[] Tweaks, string[] Avoid);

public static class ProfileMatcher
{
    public static IReadOnlyList<HardwareProfile> Match(IEnumerable<HardwareProfile> profiles, HardwareSnapshot pc) =>
        profiles.Where(profile => Matches(profile.Match, pc)).ToArray();

    public static bool Matches(ProfileMatch match, HardwareSnapshot pc)
    {
        static bool Re(string pattern, string? text) => text is not null && Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        var ramGb = pc.Memory is { } memory ? (int)Math.Round(memory.TotalBytes / 1_073_741_824d) : 0;

        return (match.Cpu is null || Re(match.Cpu, pc.Cpu?.Name))
            && (match.Gpu is null || pc.Gpus.Any(gpu => !gpu.IsIntegrated && Re(match.Gpu, gpu.Name)))
            && (match.Board is null || Re(match.Board, pc.Board?.Manufacturer))
            && (match.Memory is null || pc.Memory?.Modules.Any(module => module.Kind == match.Memory) == true)
            && (match.MinRamGb is null || ramGb >= match.MinRamGb)
            && (match.MaxRamGb is null || ramGb > 0 && ramGb <= match.MaxRamGb)
            && (match.Laptop is null || pc.Os.IsLaptop == match.Laptop);
    }
}

// ---------------------------------------------------------------- Processes (F5)

public enum ProcessCategory { Essential, Communication, Streaming, Overlay, Launcher, Optional, Bloat, VirtualMachine }

/// <param name="LowerDuringGame">Lower its priority while a game runs.</param>
public sealed record ProcessEntry(string[] Names, string Label, ProcessCategory Category, string Description, bool LowerDuringGame);

public sealed class ProcessCatalog(IEnumerable<ProcessEntry> entries)
{
    /// <summary>Never touched by the guardian, whatever the configuration says.</summary>
    public static readonly IReadOnlySet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm",
        "explorer", "fontdrvhost", "audiodg", "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "SearchHost",
        "StartMenuExperienceHost", "ShellExperienceHost", "MsMpEng", "NisSrv", "SecurityHealthService", "MpDefenderCoreService",
        "Memory Compression", "conhost", "dllhost", "spoolsv", "WmiPrvSE", "LsaIso", "vmcompute", "NovaisBoost.App"
    };

    private readonly Dictionary<string, ProcessEntry> _byName = entries
        .SelectMany(entry => entry.Names.Select(name => (name, entry)))
        .ToDictionary(item => item.name, item => item.entry, StringComparer.OrdinalIgnoreCase);

    public ProcessEntry? Find(string processName) => _byName.GetValueOrDefault(processName);

    public bool IsProtected(string processName) =>
        Protected.Contains(processName) || Find(processName)?.Category == ProcessCategory.Essential;
}

// ---------------------------------------------------------------- Games (F8)

public sealed record GameDefinition(
    string Id, string Name, string[] Executables, string AntiCheat, string? LaunchOptions, string[] Tips, string[] Folders);

public sealed record InstalledGame(GameDefinition Game, string ExecutablePath);
