using System.Globalization;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Deep diagnosis knowledge (F13)

/// <summary>Minimum microcode for a CPU family (data/microcode.json). Revisions are hex strings ("0x12B").</summary>
public sealed record MicrocodeRequirement(string Id, string Name, string Cpu, string Minimum, string Recommended, string Detail,
    string LastVerified, string Source)
{
    public uint MinimumRevision => ParseRevision(Minimum);
    public uint RecommendedRevision => ParseRevision(Recommended);

    public bool Matches(string cpuName) => Regex.IsMatch(cpuName, Cpu, RegexOptions.IgnoreCase);

    public static uint ParseRevision(string text) =>
        uint.Parse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>How a board names its memory slots (data/memory-slots.json). The first group of <see cref="Pattern"/> is the channel.</summary>
/// <param name="Board">Optional regex on the board maker; null applies to every board.</param>
public sealed record MemorySlotPattern(string Pattern, string? Board, string Example);

/// <summary>Another PC optimizer (data/optimizers.json): regexes over installed program names and process names.</summary>
public sealed record OptimizerDefinition(string Name, string[] DisplayNames, string[] Processes);

public static class KnownSoftware
{
    /// <summary>Programs that draw over the game. Each one hooks the game's frames; several at once add frame time and conflicts.</summary>
    public static readonly IReadOnlyList<(string Name, string[] Processes)> Overlays =
    [
        ("RivaTuner Statistics Server (RTSS)", ["RTSS", "RTSSHooksLoader64"]),
        ("NVIDIA Overlay", ["NVIDIA Overlay", "NVIDIA Share"]),
        ("Steam Overlay", ["GameOverlayUI"]),
        ("Xbox Game Bar", ["GameBar", "XboxGameBarWidgets"]),
        ("Discord Overlay", ["DiscordHookHelper64", "DiscordHookHelper"]),
        ("Overwolf", ["Overwolf"]),
        ("AMD Adrenalin Overlay", ["amdow"]),
        ("EA app Overlay", ["IGOProxy64", "IGOProxy"]),
        ("Ubisoft Connect Overlay", ["UplayWebCore"])
    ];

    public static IReadOnlyList<string> MatchOverlays(IEnumerable<string> processNames)
    {
        var running = processNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Overlays.Where(overlay => overlay.Processes.Any(running.Contains)).Select(overlay => overlay.Name).ToArray();
    }

    public static IReadOnlyList<string> MatchOptimizers(IEnumerable<OptimizerDefinition> definitions,
        IEnumerable<string> installedPrograms, IEnumerable<string> processNames)
    {
        var installed = installedPrograms.ToArray();
        var running = processNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return definitions.Where(definition =>
                definition.Processes.Any(running.Contains) ||
                definition.DisplayNames.Any(pattern => installed.Any(name => Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))))
            .Select(definition => definition.Name).ToArray();
    }

    /// <summary>Memory channel letter of a module ("A", "B"...), or null when the slot name is not recognized.</summary>
    public static string? ChannelOf(MemoryModule module, string? boardMaker, IEnumerable<MemorySlotPattern> patterns)
    {
        foreach (var pattern in patterns.Where(item => item.Board is null || boardMaker is not null && Regex.IsMatch(boardMaker, item.Board, RegexOptions.IgnoreCase)))
            foreach (var text in new[] { module.Slot, module.Bank }.OfType<string>())
                if (Regex.Match(text, pattern.Pattern, RegexOptions.IgnoreCase) is { Success: true } match)
                    return match.Groups[1].Value.ToUpperInvariant();
        return null;
    }
}

// ---------------------------------------------------------------- CPU under load (F13.8)

/// <summary>Averages over a short all-core load. Counters from "Processor Information" (_Total).</summary>
/// <param name="PerformancePercent">% Processor Performance: 100 = base clock, above 100 = boost.</param>
/// <param name="MinimumPerformanceLimitPercent">Lowest % Performance Limit seen: below 100 the firmware is limiting the CPU
/// (temperature, power limit or current limit).</param>
public sealed record CpuLoadSample(double Seconds, double PerformancePercent, double MinimumPerformanceLimitPercent, double MaximumFrequencyPercent);

public static class CpuLoadRules
{
    /// <summary>A performance limit below this under full load means real throttling, not a momentary dip.</summary>
    public const double LimitThreshold = 95;

    public static IReadOnlyList<Finding> Evaluate(CpuLoadSample sample)
    {
        var findings = new List<Finding>();
        if (sample.MinimumPerformanceLimitPercent < LimitThreshold)
            findings.Add(new Finding("cpu-throttling", Severity.Warning,
                $"Processador limitado sob carga: limite de desempenho em {sample.MinimumPerformanceLimitPercent:0}%",
                $"Durante {sample.Seconds:0} s de carga total o firmware reduziu o desempenho (temperatura, limite de energia ou de corrente). " +
                $"Desempenho médio: {sample.PerformancePercent:0}% do clock base.",
                "Verifique a temperatura (cooler, pasta térmica, curva das ventoinhas) e os limites de energia na BIOS. " +
                "Em notebook, use o modo de desempenho do fabricante e jogue na tomada.",
                "Processor Information: % Performance Limit, % Processor Performance"));
        else if (sample.PerformancePercent < 100)
            findings.Add(new Finding("cpu-below-base", Severity.Warning,
                $"Processador abaixo do clock base sob carga ({sample.PerformancePercent:0}%)",
                "Mesmo sem limite do firmware, a CPU não chegou ao clock base com todos os núcleos em uso.",
                "Use o plano de energia NOVAIS boost ou Alto desempenho e confira se o estado mínimo/máximo do processador não está reduzido.",
                "Processor Information: % Processor Performance"));
        return findings;
    }
}
