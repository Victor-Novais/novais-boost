using System.Text.Json;

namespace NovaisBoost.Core;

/// <param name="GamePriority">Priority of the running game. AboveNormal by default; High is opt-in.</param>
/// <param name="TouchProtectedGames">Opt-in: also change games with a kernel anti-cheat (off: the game process is never touched).</param>
/// <param name="EfficiencyMode">Put heavy background processes in efficiency mode (EcoQoS) during the game.</param>
/// <param name="BackgroundCpuSets">Move heavy background processes to E-cores / the CCD without V-Cache during the game.</param>
/// <param name="GameCpuSets">Experimental opt-in: pin the game to P-cores / the V-Cache CCD (never a protected game).</param>
/// <param name="PowerPlanDuringGame">Opt-in: NOVAIS boost plan only while a game runs, previous plan otherwise.</param>
/// <param name="StandbyPurge">Opt-in: purge the standby list when free memory is short (at most every 5 min).</param>
/// <param name="ObsCpuSets">Opt-in (F19): OBS/Streamlabs on the background cores (E-cores / CCD without V-Cache) during the game.</param>
/// <param name="WifiScanOff">F19: pause Wi-Fi background scans while a game runs (restored when it closes).</param>
/// <param name="Lower">Process names to lower during the game. <paramref name="Efficiency"/>: to put in efficiency mode.
/// <paramref name="Ignore"/>: never touched.</param>
public sealed record GuardianSettings(
    PriorityLevel GamePriority = PriorityLevel.AboveNormal,
    bool TouchProtectedGames = false,
    bool EfficiencyMode = true,
    bool BackgroundCpuSets = true,
    bool GameCpuSets = false,
    bool PowerPlanDuringGame = false,
    bool StandbyPurge = false,
    string[]? Lower = null,
    string[]? Efficiency = null,
    string[]? Ignore = null,
    bool ObsCpuSets = false,
    bool WifiScanOff = true);

/// <summary>Opt-in caches for the weekly cleanup (23.1). Browsers and launchers are only cleaned while closed.</summary>
public sealed record CleanupSettings(bool Browsers = false, bool Launchers = false);

public enum PsuRating { Unknown, White, Bronze, Silver, Gold, Platinum, Titanium }

/// <summary>What the PC cannot report about itself (23.5): the power supply, answered optionally by the user.</summary>
public sealed record PcSettings(int? PsuWatts = null, PsuRating PsuRating = PsuRating.Unknown);

/// <summary>
/// User options in %ProgramData%\NOVAIS boost\settings.json (folder writable only by administrators). Example:
/// <code>{ "guardian": { "gamePriority": "high" } }</code>
/// A missing file or field means the default; an invalid file is ignored with a warning in the log.
/// </summary>
/// <param name="Check">Look for a new version when the menu opens (only reads releases/latest.json from GitHub).</param>
public sealed record UpdateSettings(bool Check = true);

/// <param name="Enabled">Opt-in: send error reports (exception and version only) to <paramref name="Endpoint"/>.
/// Without an endpoint, reports are only saved in reports\errors.</param>
public sealed record ErrorReportSettings(bool Enabled = false, string? Endpoint = null);

/// <param name="TermsAccepted">Version of the terms of use the user accepted (<see cref="Terms.Version"/>).</param>
public sealed record AppSettings(GuardianSettings? Guardian = null, CleanupSettings? Cleanup = null, PcSettings? Pc = null,
    string? TermsAccepted = null, UpdateSettings? Updates = null, ErrorReportSettings? ErrorReports = null)
{
    public const string FileName = "settings.json";

    public static AppSettings Default { get; } = new();

    public GuardianSettings GuardianOrDefault => Guardian ?? new GuardianSettings();
    public CleanupSettings CleanupOrDefault => Cleanup ?? new CleanupSettings();
    public PcSettings PcOrDefault => Pc ?? new PcSettings();
    public UpdateSettings UpdatesOrDefault => Updates ?? new UpdateSettings();
    public ErrorReportSettings ErrorReportsOrDefault => ErrorReports ?? new ErrorReportSettings();

    public static AppSettings Parse(string json)
    {
        AppSettings settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, TweakCatalog.JsonOptions)
                ?? throw new InvalidDataException("o arquivo está vazio.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"JSON inválido (linha {(error.LineNumber ?? 0) + 1}, campo {error.Path ?? "$"}).", error);
        }
        if (settings.GuardianOrDefault.GamePriority is not (PriorityLevel.AboveNormal or PriorityLevel.High))
            throw new InvalidDataException("guardian.gamePriority aceita só \"aboveNormal\" ou \"high\".");
        if (settings.PcOrDefault.PsuWatts is { } watts && watts is < 200 or > 3000)
            throw new InvalidDataException("pc.psuWatts precisa estar entre 200 e 3000.");
        if (settings.ErrorReportsOrDefault.Endpoint is { } endpoint &&
            (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidDataException("errorReports.endpoint precisa ser um endereço https.");
        return settings;
    }

    public string Serialize() => JsonSerializer.Serialize(this, TweakCatalog.JsonOptions);

    /// <summary>Writes settings.json atomically (temporary file + replace), after validating it parses back.</summary>
    public void Save(string dataDirectory)
    {
        var json = Serialize();
        Parse(json);
        var path = Path.Combine(dataDirectory, FileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public static AppSettings Load(string dataDirectory, IActionLog log)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            return Default;
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warning($"Configurações ignoradas em {path}: {error.Message} Usando os valores padrão.");
            return Default;
        }
    }
}
