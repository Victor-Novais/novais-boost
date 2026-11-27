using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Game configuration files (F15)

public enum ConfigFormat { Ini, KeyValue, Cfg, Json }

/// <summary>
/// Reads and changes one key of a game's configuration file, keeping every other byte as it was
/// (ini, Valve KeyValues "key" "value", cfg command lines). JSON is re-serialized, so its whole-file backup matters most.
/// Fails closed: a missing or repeated key is an error, never a guess.
/// </summary>
public static partial class ConfigText
{
    public static string? Get(ConfigFormat format, string text, string? section, string key)
    {
        if (format == ConfigFormat.Json)
            return Locate(JsonNode.Parse(text), key).Node is { } node ? JsonText(node) : null;
        var match = Find(format, text, section, key);
        return match?.Groups["value"].Value;
    }

    public static string Set(ConfigFormat format, string text, string? section, string key, string value)
    {
        if (format == ConfigFormat.Json)
            return SetJson(text, key, value);
        var match = Find(format, text, section, key)
            ?? throw new InvalidDataException($"A chave {key} não existe no arquivo; o formato do jogo pode ter mudado.");
        var group = match.Groups["value"];
        return string.Concat(text.AsSpan(0, group.Index), value, text.AsSpan(group.Index + group.Length));
    }

    private static Match? Find(ConfigFormat format, string text, string? section, string key)
    {
        var (start, end) = format == ConfigFormat.Ini && section is not null ? SectionBounds(text, section) : (0, text.Length);
        if (start < 0)
            return null;
        var pattern = format switch
        {
            // key=value (value up to the end of the line, without the line break)
            ConfigFormat.Ini => $@"^[ \t]*{Regex.Escape(key)}[ \t]*=[ \t]*(?<value>[^\r\n]*?)[ \t]*(?=\r?$)",
            // "key"   "value"
            ConfigFormat.KeyValue => $@"^[ \t]*""{Regex.Escape(key)}""[ \t]+""(?<value>[^""\r\n]*)""",
            // key value  |  key "value"   (a // comment may follow)
            ConfigFormat.Cfg => $@"^[ \t]*{Regex.Escape(key)}[ \t]+(?:""(?<value>[^""\r\n]*)""|(?<value>[^\s""/]+))",
            _ => throw new InvalidDataException("Formato de configuração desconhecido.")
        };
        var matches = new Regex(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Matches(text[..end], start).Where(match => match.Index >= start).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidDataException($"A chave {key} aparece {matches.Length} vezes no arquivo; nada foi alterado.")
        };
    }

    /// <summary>Character range of an ini section body: after its [header] line up to the next [header].</summary>
    private static (int Start, int End) SectionBounds(string text, string section)
    {
        var headers = SectionHeader().Matches(text).ToArray();
        var found = headers.Where(header => string.Equals(header.Groups[1].Value.Trim(), section, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (found.Length == 0)
            return (-1, -1);
        if (found.Length > 1)
            throw new InvalidDataException($"A seção [{section}] aparece {found.Length} vezes no arquivo; nada foi alterado.");
        var start = found[0].Index + found[0].Length;
        var next = headers.FirstOrDefault(header => header.Index > found[0].Index);
        return (start, next?.Index ?? text.Length);
    }

    /// <summary>"a.b.c" → the object holding "c" and the node at "c" (null when any part is missing).</summary>
    private static (JsonObject? Parent, JsonNode? Node) Locate(JsonNode? root, string key)
    {
        var parts = key.Split('.');
        var parent = root as JsonObject;
        for (var index = 0; index < parts.Length - 1 && parent is not null; index++)
            parent = parent.TryGetPropertyValue(parts[index], out var child) ? child as JsonObject : null;
        return parent is not null && parent.TryGetPropertyValue(parts[^1], out var node) ? (parent, node) : (null, null);
    }

    private static string JsonText(JsonNode node) => node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();

    private static string SetJson(string text, string key, string value)
    {
        var root = JsonNode.Parse(text) ?? throw new InvalidDataException("JSON vazio.");
        var (parent, node) = Locate(root, key);
        if (parent is null || node is null)
            throw new InvalidDataException($"A chave {key} não existe no arquivo; o formato do jogo pode ter mudado.");
        // Keep the JSON type of the current value.
        JsonNode? replacement = node.GetValueKind() switch
        {
            JsonValueKind.Number => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? JsonValue.Create(number) : throw new InvalidDataException($"{key} espera um número."),
            JsonValueKind.True or JsonValueKind.False => bool.TryParse(value, out var flag)
                ? JsonValue.Create(flag) : throw new InvalidDataException($"{key} espera true ou false."),
            _ => JsonValue.Create(value)
        };
        parent[key.Split('.')[^1]] = replacement;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [GeneratedRegex(@"^[ \t]*\[([^\]\r\n]+)\][ \t]*\r?$", RegexOptions.Multiline)]
    private static partial Regex SectionHeader();
}

/// <summary>Byte-level operations behind <see cref="IConfigFiles"/>: encoding is kept, and revert prefers the exact original file.</summary>
public static class ConfigFiles
{
    public static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        if (bytes is [0xFF, 0xFE, ..])
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), new UTF8Encoding(true));
        return (new UTF8Encoding(false, true).GetString(bytes), new UTF8Encoding(false));
    }

    public static byte[] Encode(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    public static ConfigFileState Read(ConfigFileAction action, byte[]? bytes) => bytes is null
        ? new ConfigFileState(false, null)
        : new ConfigFileState(true, ConfigText.Get(action.Format, Decode(bytes).Text, action.Section, action.Key), Convert.ToBase64String(bytes));

    /// <summary>
    /// New file contents for <paramref name="state"/>. When restoring (the state carries the original file) and the file is
    /// exactly what this action produced from it, the original bytes come back unchanged; if the game rewrote the file
    /// since, only this key goes back to its old value.
    /// </summary>
    public static byte[] Compute(ConfigFileAction action, byte[] current, ConfigFileState state)
    {
        if (state.Value is null)
            throw new InvalidDataException($"A chave {action.Key} não tinha valor para restaurar.");
        if (state.Original is { } original)
        {
            var originalBytes = Convert.FromBase64String(original);
            var (originalText, originalEncoding) = Decode(originalBytes);
            var produced = Encode(ConfigText.Set(action.Format, originalText, action.Section, action.Key, action.Value), originalEncoding);
            if (produced.AsSpan().SequenceEqual(current))
                return originalBytes;
        }
        var (text, encoding) = Decode(current);
        return Encode(ConfigText.Set(action.Format, text, action.Section, action.Key, state.Value), encoding);
    }
}

/// <summary>Frame cap for competitive play (15.3).</summary>
public static class FrameCap
{
    /// <summary>
    /// With VRR (G-Sync/FreeSync) and Reflex: Hz − Hz²/3600 (144 → 138, 240 → 224), which keeps the game inside the VRR range.
    /// With VRR without Reflex: 3 below the refresh rate. Without VRR: null (no cap, or the game's own).
    /// </summary>
    public static int? For(int hz, bool vrr, bool reflex)
    {
        if (!vrr || hz <= 0)
            return null;
        return reflex ? (int)Math.Floor(hz - hz * hz / 3600d) : hz - 3;
    }
}

// ---------------------------------------------------------------- Per-game presets (data/games/*.json)

/// <param name="Value">Literal value, or "{frameCap}" (see <see cref="FrameCap"/>); a setting whose token has no value is skipped.</param>
public sealed record PresetSetting(string? Section, string Key, string Value, string Label);

/// <param name="Path">With tokens: %LOCALAPPDATA%, %USERPROFILE%, %APPDATA%, {steamUserdata}, {gameDir}.</param>
public sealed record PresetFile(string Path, ConfigFormat Format, PresetSetting[] Settings);

/// <param name="Processes">Game processes; files are never changed while one is running (the game rewrites them on exit).</param>
/// <param name="TestedVersion">Game version the keys were checked against on a real install; null = checked only against public sources.</param>
/// <param name="Sources">Where the keys and their meaning were confirmed.</param>
public sealed record GamePreset(string Id, string Name, string[] Processes, bool Reflex, TweakEvidence Evidence,
    string? TestedVersion, string LastVerified, string[] Sources, string Notes, PresetFile[] Files);

/// <summary>One configuration file of a preset, found on this PC (tokens already resolved).</summary>
public sealed record ResolvedPresetFile(PresetFile File, string Path, string? Account = null);

public static class GamePresets
{
    public const string FrameCapToken = "{frameCap}";

    /// <summary>Replaces %VAR% and {token} placeholders. Null when a token is unknown on this PC.</summary>
    public static string? Resolve(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var missing = false;
        var result = Regex.Replace(template, @"%(\w+)%|\{(\w+)\}", match =>
        {
            var name = match.Groups[1].Success ? $"%{match.Groups[1].Value}%" : $"{{{match.Groups[2].Value}}}";
            if (tokens.TryGetValue(name, out var value))
                return value;
            missing = true;
            return match.Value;
        });
        return missing ? null : result;
    }

    /// <summary>The preset as one tweak per file found on this PC, reviewed and backed up like every other tweak.</summary>
    public static IReadOnlyList<TweakDefinition> Build(GamePreset preset, IEnumerable<ResolvedPresetFile> files, int? frameCap)
    {
        var tweaks = new List<TweakDefinition>();
        foreach (var file in files)
        {
            var actions = file.File.Settings
                .Select(setting => (setting, value: setting.Value == FrameCapToken
                    ? frameCap?.ToString("0.000000", CultureInfo.InvariantCulture) : setting.Value))
                .Where(item => item.value is not null)
                .Select(item => (TweakAction)new ConfigFileAction(file.Path, file.File.Format, item.setting.Section, item.setting.Key,
                    item.value!, preset.Processes))
                .ToArray();
            if (actions.Length == 0)
                continue;
            var labels = file.File.Settings.Where(setting => setting.Value != FrameCapToken || frameCap is not null)
                .Select(setting => setting.Value == FrameCapToken ? $"{setting.Label} ({frameCap} FPS)" : setting.Label);
            var suffix = file.Account is null ? "" : $" (conta {file.Account})";
            tweaks.Add(new TweakDefinition(
                $"game-{preset.Id}-{tweaks.Count + 1}", $"{preset.Name}: preset competitivo{suffix}",
                $"Altera o arquivo de configuração do jogo: {string.Join("; ", labels)}. {preset.Notes} " +
                $"Arquivo: {file.Path}. O arquivo inteiro é copiado para o backup antes da primeira mudança.",
                $"Jogos: {preset.Name}", TweakLevel.Safe, preset.Evidence,
                "Baixo. Feche o jogo antes; ao reverter, o arquivo volta igual ao original.", false,
                new TweakCompatibility([], []), actions));
        }
        return tweaks;
    }
}
