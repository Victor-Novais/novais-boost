namespace NovaisBoost.Core;

public enum PriorityLevel { Idle, BelowNormal, Normal, AboveNormal, High }

/// <param name="WorkingSetBytes">Used by the memory leak alert; 0 when unknown.</param>
public sealed record ProcessSample(int Id, string Name, PriorityLevel Priority, double CpuPercent, long WorkingSetBytes = 0);

public sealed record PriorityChange(int Id, string Name, PriorityLevel From, PriorityLevel To, string Reason);

/// <summary>
/// Pure decision logic of the Process Guardian. Given the current processes and the games list, returns what to change.
/// Restoring the previous state is done by the caller. A game protected by an anti-cheat is never touched unless the
/// user opts in (guardian.touchProtectedGames).
/// </summary>
/// <param name="gamePriority">Priority given to the game: AboveNormal (default) or High (opt-in in settings.json).</param>
/// <param name="protectedGameExecutables">Games with a kernel anti-cheat (see <see cref="GameDefinition.ProtectedByAntiCheat"/>).</param>
public sealed class GuardianPolicy(ProcessCatalog catalog, IEnumerable<string> gameExecutables,
    PriorityLevel gamePriority = PriorityLevel.AboveNormal, GuardianSettings? settings = null,
    IEnumerable<string>? protectedGameExecutables = null)
{
    /// <summary>A background process above this share of total CPU while a game runs is lowered (ProBalance).</summary>
    public const double HogCpuPercent = 15;

    public GuardianSettings Settings { get; } = settings ?? new GuardianSettings();

    public PriorityLevel GamePriority { get; } = gamePriority is PriorityLevel.AboveNormal or PriorityLevel.High
        ? gamePriority
        : throw new ArgumentOutOfRangeException(nameof(gamePriority), "A prioridade do jogo deve ser AboveNormal ou High.");

    private readonly HashSet<string> _games = Names(gameExecutables);
    private readonly HashSet<string> _protected = Names(protectedGameExecutables ?? []);
    private readonly HashSet<string> _ignore = Names((settings ?? new()).Ignore ?? []);
    private readonly HashSet<string> _lower = Names((settings ?? new()).Lower ?? []);
    private readonly HashSet<string> _efficiency = Names((settings ?? new()).Efficiency ?? []);

    public bool IsGame(string processName) => _games.Contains(processName);

    /// <summary>A game process the Guardian may change: not protected by an anti-cheat, or the user opted in.</summary>
    public bool MayTouchGame(string processName) => IsGame(processName) && (!_protected.Contains(processName) || Settings.TouchProtectedGames);

    public IReadOnlyList<PriorityChange> Plan(IReadOnlyList<ProcessSample> processes)
    {
        var changes = new List<PriorityChange>();
        if (!processes.Any(process => IsGame(process.Name)))
            return changes;

        foreach (var process in processes)
        {
            if (IsGame(process.Name))
            {
                if (MayTouchGame(process.Name) && process.Priority < GamePriority)
                    changes.Add(new(process.Id, process.Name, process.Priority, GamePriority, "jogo em execução"));
                continue;
            }
            if (!MayTouchBackground(process.Name))
                continue;

            var entry = catalog.Find(process.Name);
            if (entry?.Category == ProcessCategory.Streaming)
            {
                if (process.Priority < PriorityLevel.AboveNormal)
                    changes.Add(new(process.Id, process.Name, process.Priority, PriorityLevel.AboveNormal, "transmissão durante o jogo"));
                continue;
            }
            if (entry?.Category is ProcessCategory.Communication or ProcessCategory.Overlay && !_lower.Contains(process.Name))
                continue;

            var reason = BackgroundReason(process, entry);
            if (reason is not null && process.Priority > PriorityLevel.BelowNormal)
                changes.Add(new(process.Id, process.Name, process.Priority, PriorityLevel.BelowNormal, reason));
        }
        return changes;
    }

    /// <summary>17.2: background processes to put in Windows' efficiency mode (EcoQoS) while a game runs.</summary>
    public IReadOnlyList<int> EfficiencyTargets(IReadOnlyList<ProcessSample> processes) =>
        Settings.EfficiencyMode ? HeavyBackground(processes, includeEfficiencyList: true) : [];

    /// <summary>17.3: heavy background processes to move to the background CPU set (E-cores, or the CCD without V-Cache).</summary>
    public IReadOnlyList<int> CpuSetTargets(IReadOnlyList<ProcessSample> processes) =>
        Settings.BackgroundCpuSets ? HeavyBackground(processes, includeEfficiencyList: false) : [];

    /// <summary>17.4 (experimental, opt-in): game processes to pin to the game CPU set. Never a protected game.</summary>
    public IReadOnlyList<int> GameCpuSetTargets(IReadOnlyList<ProcessSample> processes) => Settings.GameCpuSets
        ? processes.Where(process => IsGame(process.Name) && !_protected.Contains(process.Name)).Select(process => process.Id).ToArray()
        : [];

    private int[] HeavyBackground(IReadOnlyList<ProcessSample> processes, bool includeEfficiencyList)
    {
        if (!processes.Any(process => IsGame(process.Name)))
            return [];
        return processes.Where(process => !IsGame(process.Name) && MayTouchBackground(process.Name))
            .Where(process => catalog.Find(process.Name)?.Category is not (ProcessCategory.Streaming or ProcessCategory.Communication or ProcessCategory.Overlay)
                || _lower.Contains(process.Name))
            .Where(process => BackgroundReason(process, catalog.Find(process.Name)) is not null ||
                includeEfficiencyList && _efficiency.Contains(process.Name))
            .Select(process => process.Id).ToArray();
    }

    private bool MayTouchBackground(string name) => !catalog.IsProtected(name) && !_ignore.Contains(name);

    private string? BackgroundReason(ProcessSample process, ProcessEntry? entry) =>
        _lower.Contains(process.Name) ? "na lista do usuário para baixar durante o jogo"
        : entry?.LowerDuringGame == true ? $"{entry.Label} em segundo plano"
        : process.CpuPercent >= HogCpuPercent ? $"usando {process.CpuPercent:0}% da CPU durante o jogo"
        : null;

    private static HashSet<string> Names(IEnumerable<string> names) =>
        names.Select(name => WindowsPath.GetFileNameWithoutExtension(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

// ---------------------------------------------------------------- CPU topology (17.3, 17.4)

/// <param name="EfficiencyClass">0 = most efficient (E-core); higher = faster (P-core). All equal on non-hybrid CPUs.</param>
/// <param name="LastLevelCacheIndex">Which L3 the core uses (one per CCD on Ryzen).</param>
public sealed record CpuSetInfo(uint Id, byte EfficiencyClass, byte LastLevelCacheIndex);

/// <param name="CacheBytesByLlc">L3 size of each LastLevelCacheIndex (the X3D CCD has the larger one).</param>
public sealed record CpuTopology(IReadOnlyList<CpuSetInfo> Sets, IReadOnlyDictionary<byte, long> CacheBytesByLlc)
{
    public bool IsHybrid => Sets.Select(set => set.EfficiencyClass).Distinct().Count() > 1;

    /// <summary>Two or more L3 caches of different sizes: a Ryzen X3D with 2 CCDs.</summary>
    public bool HasAsymmetricCache => CacheBytesByLlc.Count > 1 && CacheBytesByLlc.Values.Distinct().Count() > 1;

    /// <summary>Where background work goes: E-cores on hybrid Intel, the CCD without 3D V-Cache on dual-CCD X3D; empty otherwise.</summary>
    public IReadOnlyList<uint> BackgroundSets()
    {
        IReadOnlyList<uint> sets = IsHybrid
            ? Sets.Where(set => set.EfficiencyClass == Sets.Min(item => item.EfficiencyClass)).Select(set => set.Id).ToArray()
            : HasAsymmetricCache
                ? Sets.Where(set => set.LastLevelCacheIndex == CacheBytesByLlc.MinBy(pair => pair.Value).Key).Select(set => set.Id).ToArray()
                : [];
        return sets.Count > 0 && sets.Count < Sets.Count ? sets : [];
    }

    /// <summary>Where the game goes (opt-in): P-cores, or the CCD with 3D V-Cache.</summary>
    public IReadOnlyList<uint> GameSets()
    {
        IReadOnlyList<uint> sets = IsHybrid
            ? Sets.Where(set => set.EfficiencyClass == Sets.Max(item => item.EfficiencyClass)).Select(set => set.Id).ToArray()
            : HasAsymmetricCache
                ? Sets.Where(set => set.LastLevelCacheIndex == CacheBytesByLlc.MaxBy(pair => pair.Value).Key).Select(set => set.Id).ToArray()
                : [];
        return sets.Count > 0 && sets.Count < Sets.Count ? sets : [];
    }
}

// ---------------------------------------------------------------- Power plan, standby list, alerts (17.5, 17.6, 17.8)

public static class GuardianRules
{
    public const long PurgeFreeBelowBytes = 1L << 30;
    public const long PurgeStandbyAboveBytes = 2L << 30;
    public static readonly TimeSpan PurgeInterval = TimeSpan.FromMinutes(5);
    public const double RamAlertPercent = 90;
    public const double VramAlertPercent = 95;

    /// <summary>17.5: switch to the NOVAIS boost plan only while a game runs; never on dual-CCD X3D (they need Balanced).</summary>
    public static bool UseGamePowerPlan(GuardianSettings settings, bool gameRunning, bool dualCcdX3d, bool planInstalled) =>
        settings.PowerPlanDuringGame && gameRunning && !dualCcdX3d && planInstalled;

    /// <summary>17.6: purge the standby list only when free memory is really short and the standby list is big, at most every 5 min.</summary>
    public static bool ShouldPurgeStandby(GuardianSettings settings, long freeBytes, long standbyBytes, DateTimeOffset? lastPurge, DateTimeOffset now) =>
        settings.StandbyPurge && freeBytes < PurgeFreeBelowBytes && standbyBytes > PurgeStandbyAboveBytes &&
        (lastPurge is null || now - lastPurge >= PurgeInterval);

    /// <summary>17.8: RAM and VRAM pressure alerts (null percentages are unknown and never alert).</summary>
    public static IReadOnlyList<string> Alerts(double? ramUsedPercent, double? vramUsedPercent)
    {
        var alerts = new List<string>();
        if (ramUsedPercent >= RamAlertPercent)
            alerts.Add($"RAM em {ramUsedPercent:0}%: feche programas abertos atrás do jogo para evitar travadas.");
        if (vramUsedPercent >= VramAlertPercent)
            alerts.Add($"Memória de vídeo em {vramUsedPercent:0}%: baixe a qualidade de texturas para evitar travadas.");
        return alerts;
    }
}

/// <summary>17.8: a process whose working set only grows for 30 minutes (by at least 25% and 300 MB) is probably leaking.</summary>
public sealed class LeakTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    public const long MinimumGrowthBytes = 300L << 20;
    public const double MinimumGrowthFraction = 0.25;

    /// <summary>Drops below this fraction of the previous sample count as "not growing" (the process freed memory).</summary>
    private const double Tolerance = 0.97;

    private readonly Dictionary<string, List<(DateTimeOffset Time, long Bytes)>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a sample per process name and returns the names that just started leaking (each reported once).</summary>
    public IReadOnlyList<string> Record(DateTimeOffset now, IEnumerable<ProcessSample> processes)
    {
        var leaking = new List<string>();
        foreach (var group in processes.Where(process => process.WorkingSetBytes > 0).GroupBy(process => process.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!_history.TryGetValue(group.Key, out var samples))
                _history[group.Key] = samples = [];
            samples.Add((now, group.Sum(process => process.WorkingSetBytes)));
            samples.RemoveAll(sample => now - sample.Time > Window + TimeSpan.FromMinutes(5));
            if (!_reported.Contains(group.Key) && IsLeaking(samples, now))
            {
                _reported.Add(group.Key);
                leaking.Add(group.Key);
            }
        }
        return leaking;
    }

    private static bool IsLeaking(List<(DateTimeOffset Time, long Bytes)> samples, DateTimeOffset now)
    {
        var window = samples.Where(sample => now - sample.Time <= Window).ToArray();
        if (window.Length < 3 || now - window[0].Time < Window - TimeSpan.FromMinutes(1))
            return false;
        for (var index = 1; index < window.Length; index++)
            if (window[index].Bytes < window[index - 1].Bytes * Tolerance)
                return false;
        var growth = window[^1].Bytes - window[0].Bytes;
        return growth >= MinimumGrowthBytes && growth >= window[0].Bytes * MinimumGrowthFraction;
    }
}
