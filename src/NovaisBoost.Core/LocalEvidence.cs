using System.Text.Json;

namespace NovaisBoost.Core;

/// <summary>Result of an A/B test ("Provar ajustes") for one tweak of the tested group, on this PC.</summary>
/// <param name="OnePercentLowChange">Change of the 1% low, in %, with its 95% interval.</param>
public sealed record LocalEvidenceEntry(string TweakId, string Group, string Game, Verdict Verdict,
    double AverageFpsChange, double OnePercentLowChange, double CiLowPercent, double CiHighPercent, DateTimeOffset MeasuredAt);

/// <summary>
/// Evidence measured on this PC, kept in %ProgramData%\NOVAIS boost\evidence.json. The latest test of a tweak wins:
/// a real gain makes it "measured" here (it can enter the levels); a proven loss keeps it out of the levels here.
/// </summary>
public sealed class LocalEvidence
{
    public const string FileName = "evidence.json";

    private readonly Dictionary<string, LocalEvidenceEntry> _latest;

    public LocalEvidence(IEnumerable<LocalEvidenceEntry> entries)
    {
        Entries = entries.OrderBy(entry => entry.MeasuredAt).ToArray();
        _latest = Entries.GroupBy(entry => entry.TweakId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    public static LocalEvidence Empty { get; } = new([]);

    public IReadOnlyList<LocalEvidenceEntry> Entries { get; }

    public LocalEvidenceEntry? Latest(string tweakId) => _latest.GetValueOrDefault(tweakId);

    public TweakEvidence Effective(TweakDefinition tweak) =>
        Latest(tweak.Id)?.Verdict == Verdict.RealGain ? TweakEvidence.Measured : tweak.Evidence;

    public bool IsWorse(string tweakId) => Latest(tweakId)?.Verdict == Verdict.Worse;

    public LocalEvidence With(IEnumerable<LocalEvidenceEntry> entries) => new(Entries.Concat(entries));

    public static LocalEvidence Load(string dataDirectory, IActionLog log)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return Empty;
        try
        {
            return new LocalEvidence(JsonSerializer.Deserialize<LocalEvidenceEntry[]>(File.ReadAllText(path), TweakCatalog.JsonOptions) ?? []);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            log.Warning($"Evidências locais ignoradas em {path}: {error.Message}");
            return Empty;
        }
    }

    public void Save(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Entries, TweakCatalog.JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
