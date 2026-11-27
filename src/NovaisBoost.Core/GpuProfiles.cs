namespace NovaisBoost.Core;

/// <summary>
/// NVIDIA driver settings per game (F16.1). Setting ids were checked against the installed driver with
/// NvAPI_DRS_GetSettingNameFromId; values only where NVIDIA's own profiles confirm them.
/// </summary>
public static class NvidiaProfiles
{
    /// <summary>"Power management mode"; 1 = prefer maximum performance (the value NVIDIA's predefined CS2 profile uses).</summary>
    public const uint PowerManagementMode = 0x1057EB71, PreferMaximumPerformance = 1;

    /// <summary>"Frame Rate Limiter" (Max Frame Rate). The value is the FPS cap.</summary>
    public const uint FrameRateLimiter = 0x10835002;

    /// <summary>
    /// For each game: power mode at maximum performance in the game's own profile (documented NVIDIA option), and,
    /// with a VRR frame cap, the driver frame limiter as an alternative to the in-game cap (experimental: the value
    /// format was not confirmed against the driver).
    /// </summary>
    public static IReadOnlyList<TweakDefinition> Build(IEnumerable<InstalledGame> games, int? frameCap)
    {
        var tweaks = new List<TweakDefinition>();
        foreach (var group in games.Where(game => game.Game.Id != "obs").GroupBy(game => game.Game.Id))
        {
            var game = group.First().Game;
            var exes = group.Select(item => WindowsPath.GetFileName(item.ExecutablePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var compat = new TweakCompatibility(["gpu-nvidia"], []);
            tweaks.Add(new TweakDefinition($"nvidia-profile-{game.Id}", $"NVIDIA: {game.Name} em desempenho máximo",
                $"No perfil do {game.Name} no driver NVIDIA ({string.Join(", ", exes)}), o modo de energia fica em \"Preferir desempenho máximo\". " +
                "Vale só para o jogo, sem deixar a GPU em clock alto no resto do tempo. Se o jogo não tiver perfil, um perfil \"NOVAIS boost\" é criado.",
                $"Jogos: {game.Name}", TweakLevel.Safe, TweakEvidence.Documented, "Baixo. A GPU consome mais energia com o jogo aberto.", false, compat,
                exes.Select(exe => (TweakAction)new NvidiaProfileAction(exe, PowerManagementMode, "Power management mode", PreferMaximumPerformance)).ToArray()));
            if (frameCap is { } cap)
                tweaks.Add(new TweakDefinition($"nvidia-fps-limit-{game.Id}", $"NVIDIA: limite de {cap} FPS no {game.Name}",
                    $"Limita o {game.Name} a {cap} FPS pelo driver (Max Frame Rate), para ficar dentro da faixa do G-Sync. " +
                    "Use só se o jogo não tiver limite de FPS próprio; com Reflex, o limite do jogo é melhor.",
                    $"Jogos: {game.Name}", TweakLevel.Safe, TweakEvidence.Experimental, "Baixo. Com o limite, o FPS não passa desse valor.", false, compat,
                    exes.Select(exe => (TweakAction)new NvidiaProfileAction(exe, FrameRateLimiter, "Frame Rate Limiter", (uint)cap)).ToArray()));
        }
        return tweaks;
    }
}

/// <summary>Radeon Software settings for competitive play (F16.2), applied only where ADLX is available.</summary>
public static class AmdProfiles
{
    public static TweakDefinition Competitive() => new("amd-competitive", "AMD Radeon: ajustes competitivos",
        "Liga o Radeon Anti-Lag e desliga Radeon Chill, Radeon Boost, Enhanced Sync e HYPR-RX, que trocam latência ou nitidez por FPS ou economia.",
        "GPU", TweakLevel.Competitive, TweakEvidence.Documented, "Baixo. Pode ser revertido no Adrenalin ou em Reverter / Backups.", false,
        new TweakCompatibility(["gpu-discrete"], []),
        [new AmdSettingAction("AntiLag", true), new AmdSettingAction("Chill", false), new AmdSettingAction("Boost", false),
            new AmdSettingAction("EnhancedSync", false), new AmdSettingAction("HyprRx", false)]);
}
