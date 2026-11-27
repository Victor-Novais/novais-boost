using System.Text.Json;

namespace NovaisBoost.Core;

/// <param name="GamePriority">Priority of the running game. AboveNormal by default; High is opt-in.</param>
public sealed record GuardianSettings(PriorityLevel GamePriority = PriorityLevel.AboveNormal);

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
