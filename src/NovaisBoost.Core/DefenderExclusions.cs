namespace NovaisBoost.Core;

/// <summary>17.7: opt-in Defender exclusions for the folders of installed games (instead of turning the antivirus off).</summary>
public static class DefenderExclusions
{
    /// <summary>
    /// One tweak with an exclusion per game folder. Folders too broad to exclude safely (a drive root, Windows,
    /// Program Files, the user profile itself) are left out.
    /// </summary>
    public static TweakDefinition? Build(IEnumerable<InstalledGame> games, IEnumerable<string> forbiddenFolders)
    {
        var forbidden = forbiddenFolders.Select(Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = games.Where(game => game.Game.Id != "obs")
            .Select(game => WindowsPath.GetDirectoryName(game.ExecutablePath))
            .OfType<string>().Select(Normalize)
            .Where(folder => WindowsPath.GetRoot(folder) is { } root && !string.Equals(root, folder, StringComparison.OrdinalIgnoreCase))
            .Where(folder => !forbidden.Contains(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return folders.Length == 0 ? null : new TweakDefinition("defender-game-folders", "Exclusões do Defender para as pastas dos jogos",
            $"O Microsoft Defender deixa de verificar em tempo real: {string.Join(", ", folders)}. Reduz travadas ao carregar mapas e " +
            "atualizar o jogo. O Defender continua ligado para todo o resto do PC.",
            "Segurança", TweakLevel.Competitive, TweakEvidence.Documented,
            "Médio. Um arquivo malicioso colocado nessas pastas não seria verificado ao abrir. Só use com jogos de lojas oficiais.", false,
            new TweakCompatibility([], []), folders.Select(folder => (TweakAction)new DefenderExclusionAction(folder)).ToArray());
    }

    private static string Normalize(string path) => WindowsPath.Normalize(path);
}
