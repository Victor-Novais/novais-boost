using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Finds installed games in Steam, Epic, Riot, Battle.net, Xbox and Program Files folders, plus running processes.</summary>
internal static partial class GameLibrary
{
    private static readonly EnumerationOptions Search = new()
    {
        RecurseSubdirectories = true, MaxRecursionDepth = 7, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static IReadOnlyList<InstalledGame> Detect(IReadOnlyList<GameDefinition> games, IActionLog log)
    {
        var roots = Roots(log).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new Dictionary<string, InstalledGame>(StringComparer.OrdinalIgnoreCase);

        foreach (var game in games)
        {
            var names = game.Executables.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                IEnumerable<string> folders;
                try
                {
                    folders = Directory.EnumerateDirectories(root)
                        .Where(folder => game.Folders.Any(name => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                foreach (var folder in folders)
                foreach (var exe in Directory.EnumerateFiles(folder, "*.exe", Search).Where(path => names.Contains(Path.GetFileName(path))))
                    found.TryAdd(exe, new InstalledGame(game, exe));
            }
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var game = games.FirstOrDefault(game => game.Executables.Any(exe =>
                    string.Equals(Path.GetFileNameWithoutExtension(exe), process.ProcessName, StringComparison.OrdinalIgnoreCase)));
                if (game is null)
                    continue;
                try
                {
                    if (process.MainModule?.FileName is { } path)
                        found.TryAdd(path, new InstalledGame(game, path));
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Protected (anti-cheat) processes do not expose their path.
                }
            }
        }

        log.Info($"Jogos detectados: {found.Count}.");
        return found.Values.OrderBy(item => item.Game.Name, StringComparer.CurrentCulture).ToArray();
    }

    private static IEnumerable<string> Roots(IActionLog log)
    {
        foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } programFiles)
                yield return programFiles;
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            yield return Path.Combine(drive.Name, "Riot Games");
            yield return Path.Combine(drive.Name, "XboxGames");
            yield return Path.Combine(drive.Name, "Games");
            yield return Path.Combine(drive.Name, "Epic Games");
        }

        foreach (var library in SteamLibraries(log))
            yield return Path.Combine(library, "steamapps", "common");

        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
        if (Directory.Exists(manifests))
        {
            foreach (var manifest in Directory.EnumerateFiles(manifests, "*.item"))
            {
                string? location = null;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                    location = document.RootElement.TryGetProperty("InstallLocation", out var value) ? value.GetString() : null;
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
                {
                    log.Warning($"Manifesto da Epic ilegível: {manifest}.");
                }
                if (location is not null && Path.GetDirectoryName(location) is { } parent)
                    yield return parent;
            }
        }
    }

    private static IEnumerable<string> SteamLibraries(IActionLog log)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam)
            yield break;
        steam = steam.Replace('/', '\\');
        yield return steam;

        var file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        string text;
        try
        {
            text = File.Exists(file) ? File.ReadAllText(file) : "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Biblioteca da Steam ilegível: {error.Message}");
            yield break;
        }
        foreach (Match match in SteamPath().Matches(text))
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex SteamPath();
}
