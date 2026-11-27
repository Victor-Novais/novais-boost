using System.Diagnostics;
using Microsoft.Win32;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Game configuration files on disk. The byte-level logic (encoding, exact restore) is in Core.ConfigFiles.</summary>
internal sealed class WindowsConfigFiles : IConfigFiles
{
    public ConfigFileState Read(ConfigFileAction action) =>
        ConfigFiles.Read(action, File.Exists(action.Path) ? File.ReadAllBytes(action.Path) : null);

    public void Write(ConfigFileAction action, ConfigFileState state)
    {
        var info = new FileInfo(action.Path);
        if (!info.Exists)
            throw new FileNotFoundException($"{info.Name} não existe mais.", action.Path);
        if (info.IsReadOnly)
            throw new InvalidOperationException($"{info.Name} está como somente leitura (o jogo ou você travou o arquivo). " +
                "Nada foi alterado. Tire o \"Somente leitura\" nas propriedades do arquivo se quiser aplicar.");

        var updated = ConfigFiles.Compute(action, File.ReadAllBytes(action.Path), state);
        // Atomic replace: the game never sees a half-written file.
        var temporary = action.Path + ".novais-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            File.WriteAllBytes(temporary, updated);
            File.Move(temporary, action.Path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public bool IsInUse(ConfigFileAction action) => action.LockedBy.Any(name =>
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    });
}

/// <summary>Finds the files of each game preset on this PC and turns the presets into reviewable tweaks.</summary>
internal static class GamePresetFiles
{
    /// <summary>Presets for the installed games, with their files resolved (one per Steam account for Steam games).</summary>
    public static IReadOnlyList<(GamePreset Preset, IReadOnlyList<ResolvedPresetFile> Files)> Find(IReadOnlyList<InstalledGame> installed)
    {
        var ids = installed.Select(game => game.Game.Id).ToHashSet(StringComparer.Ordinal);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%LOCALAPPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["%APPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["%USERPROFILE%"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        var steamAccounts = SteamUserdata();

        var result = new List<(GamePreset, IReadOnlyList<ResolvedPresetFile>)>();
        foreach (var preset in DataCatalog.Presets().Where(preset => ids.Contains(preset.Id)))
        {
            var files = new List<ResolvedPresetFile>();
            foreach (var file in preset.Files)
            {
                if (file.Path.Contains("{steamUserdata}", StringComparison.OrdinalIgnoreCase))
                {
                    // Only accounts that already played the game (the file exists), so other Steam users are not listed.
                    foreach (var (account, folder) in steamAccounts)
                        if (GamePresets.Resolve(file.Path, new Dictionary<string, string>(tokens, StringComparer.OrdinalIgnoreCase) { ["{steamUserdata}"] = folder }) is { } path &&
                            File.Exists(path))
                            files.Add(new ResolvedPresetFile(file, path, account));
                }
                else if (GamePresets.Resolve(file.Path, tokens) is { } path)
                    files.Add(new ResolvedPresetFile(file, path));
            }
            if (files.Count > 0)
                result.Add((preset, files));
        }
        return result;
    }

    private static IReadOnlyList<(string Account, string Folder)> SteamUserdata()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("SteamPath") is not string steam)
            return [];
        var userdata = Path.Combine(steam.Replace('/', '\\'), "userdata");
        return Directory.Exists(userdata)
            ? Directory.EnumerateDirectories(userdata).Select(folder => (Path.GetFileName(folder), folder))
                .Where(item => item.Item1.All(char.IsDigit) && item.Item1 != "0").ToArray()
            : [];
    }
}
