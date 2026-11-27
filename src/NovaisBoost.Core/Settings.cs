using System.Text.Json;

namespace NovaisBoost.Core;

/// <param name="GamePriority">Priority of the running game. AboveNormal by default; High is opt-in.</param>
/// <param name="TouchProtectedGames">Opt-in: also change games with a kernel anti-cheat (off: the game process is never touched).</param>
/// <param name="EfficiencyMode">Put heavy background processes in efficiency mode (EcoQoS) during the game.</param>
/// <param name="BackgroundCpuSets">Move heavy background processes to E-cores / the CCD without V-Cache during the game.</param>
/// <param name="GameCpuSets">Experimental opt-in: pin the game to P-cores / the V-Cache CCD (never a protected game).</param>
/// <param name="PowerPlanDuringGame">Opt-in: NOVAIS boost plan only while a game runs, previous plan otherwise.</param>
/// <param name="StandbyPurge">Opt-in: purge the standby list when free memory is short (at most every 5 min).</param>
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
    string[]? Ignore = null);

/// <summary>
/// User options in %ProgramData%\NOVAIS boost\settings.json (folder writable only by administrators). Example:
/// <code>{ "guardian": { "gamePriority": "high" } }</code>
/// A missing file or field means the default; an invalid file is ignored with a warning in the log.
/// </summary>
public sealed record AppSettings(GuardianSettings? Guardian = null)
{
    public const string FileName = "settings.json";

    public static AppSettings Default { get; } = new();

    public GuardianSettings GuardianOrDefault => Guardian ?? new GuardianSettings();

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
        return settings;
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
