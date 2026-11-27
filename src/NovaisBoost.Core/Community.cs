using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

/// <param name="Enabled">Opt-in (off by default): send A/B results and read what other PCs measured.</param>
/// <param name="Prompted">The consent screen was already shown once.</param>
/// <param name="Endpoint">Base address of the service (POST /v1/results, GET /v1/evidence).</param>
public sealed record CommunitySettings(bool Enabled = false, bool Prompted = false, string? Endpoint = null);

/// <summary>
/// What one A/B test sends (F21). Hardware models, the Windows build, the tested tweaks and the relative result only:
/// never a name, e-mail, serial number, IP, machine name or the list of programs. The date is the month only.
/// </summary>
public sealed record CommunityResult(string Cpu, string Gpu, int RamGb, int RamMts, string WindowsBuild, string Month,
    string[] Tweaks, string Group, string Game, string Verdict, double AverageFpsChange, double OnePercentLowChange,
    double CiLowPercent, double CiHighPercent, string AppVersion);

/// <summary>Aggregate for one tweak on one hardware class, as returned by GET /v1/evidence.</summary>
public sealed record CommunityEvidence(string Tweak, int Samples, double OnePercentLowChange, double CiLowPercent, double CiHighPercent);

public static partial class Community
{
    /// <summary>A tweak becomes "measured" for a hardware class with at least this many results...</summary>
    public const int MinSamples = 30;

    public static CommunityResult Build(HardwareSnapshot pc, IReadOnlyList<LocalEvidenceEntry> entries, string appVersion)
    {
        var first = entries.Count > 0 ? entries[0] : throw new ArgumentException("Sem resultado para enviar.", nameof(entries));
        var gpu = pc.Gpus.FirstOrDefault(item => !item.IsIntegrated) ?? pc.Gpus.FirstOrDefault();
        var modules = pc.Memory?.Modules ?? [];
        return new CommunityResult(Model(pc.Cpu?.Name), Model(gpu?.Name),
            pc.Memory is { } memory ? (int)Math.Round(memory.TotalBytes / 1_073_741_824d) : 0,
            modules.Count == 0 ? 0 : modules.Max(module => module.ConfiguredMts),
            pc.Os.Build, first.MeasuredAt.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            entries.Select(entry => entry.TweakId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            first.Group, first.Game, first.Verdict.ToString(),
            Math.Round(first.AverageFpsChange, 2), Math.Round(first.OnePercentLowChange, 2),
            Math.Round(first.CiLowPercent, 2), Math.Round(first.CiHighPercent, 2), appVersion);
    }

    /// <summary>"AMD Ryzen 5 7600X 6-Core Processor" → "AMD Ryzen 5 7600X"; trademarks and core counts removed.</summary>
    public static string Model(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "desconhecido";
        var clean = Trademarks().Replace(name, "");
        clean = CoreCount().Replace(clean, "");
        clean = Frequency().Replace(clean, "");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        return clean.Length > 60 ? clean[..60] : clean;
    }

    public static string ToJson(CommunityResult result) => JsonSerializer.Serialize(result, TweakCatalog.JsonOptions);

    public static IReadOnlyList<CommunityEvidence> ParseEvidence(string json) =>
        JsonSerializer.Deserialize<CommunityEvidence[]>(json, TweakCatalog.JsonOptions) ?? [];

    /// <summary>...and a 95% interval of the 1% low change entirely above zero.</summary>
    public static bool IsProven(CommunityEvidence evidence) => evidence.Samples >= MinSamples && evidence.CiLowPercent > 0;

    public static bool IsProvenWorse(CommunityEvidence evidence) => evidence.Samples >= MinSamples && evidence.CiHighPercent < 0;

    /// <summary>"Testado em 214 PCs parecidos com o seu: +3,1% no 1% low (IC 95%: +1,8 a +4,4)".</summary>
    public static string Describe(CommunityEvidence evidence)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        string Signed(double value) => value.ToString("+0.0;-0.0;0,0", culture);
        return $"Testado em {evidence.Samples} PCs parecidos com o seu: {Signed(evidence.OnePercentLowChange)}% no 1% low " +
            $"(IC 95%: {Signed(evidence.CiLowPercent)} a {Signed(evidence.CiHighPercent)})";
    }

    [GeneratedRegex(@"\((R|TM|tm|r)\)|®|™|\bCPU\b|\bProcessor\b|\bwith Radeon Graphics\b", RegexOptions.IgnoreCase)]
    private static partial Regex Trademarks();

    [GeneratedRegex(@"\b\d+-Core\b", RegexOptions.IgnoreCase)]
    private static partial Regex CoreCount();

    [GeneratedRegex(@"@\s*[\d.]+\s*GHz", RegexOptions.IgnoreCase)]
    private static partial Regex Frequency();
}
