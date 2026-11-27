namespace NovaisBoost.Core;

public enum PriorityLevel { Idle, BelowNormal, Normal, AboveNormal, High }

public sealed record ProcessSample(int Id, string Name, PriorityLevel Priority, double CpuPercent);

public sealed record PriorityChange(int Id, string Name, PriorityLevel From, PriorityLevel To, string Reason);

/// <summary>
/// Pure decision logic of the Process Guardian. Given the current processes and the games list,
/// returns the priority changes to make. Restoring previous priorities is done by the caller.
/// </summary>
/// <param name="gamePriority">Priority given to the game: AboveNormal (default) or High (opt-in in settings.json).</param>
public sealed class GuardianPolicy(ProcessCatalog catalog, IEnumerable<string> gameExecutables,
    PriorityLevel gamePriority = PriorityLevel.AboveNormal)
{
    /// <summary>A background process above this share of total CPU while a game runs is lowered (ProBalance).</summary>
    public const double HogCpuPercent = 15;

    public PriorityLevel GamePriority { get; } = gamePriority is PriorityLevel.AboveNormal or PriorityLevel.High
        ? gamePriority
        : throw new ArgumentOutOfRangeException(nameof(gamePriority), "A prioridade do jogo deve ser AboveNormal ou High.");

    private readonly HashSet<string> _games = gameExecutables
        .Select(name => Path.GetFileNameWithoutExtension(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsGame(string processName) => _games.Contains(processName);

    public IReadOnlyList<PriorityChange> Plan(IReadOnlyList<ProcessSample> processes)
    {
        var changes = new List<PriorityChange>();
        if (!processes.Any(process => IsGame(process.Name)))
            return changes;

        foreach (var process in processes)
        {
            if (IsGame(process.Name))
            {
                if (process.Priority < GamePriority)
                    changes.Add(new(process.Id, process.Name, process.Priority, GamePriority, "jogo em execução"));
                continue;
            }
            if (catalog.IsProtected(process.Name))
                continue;

            var entry = catalog.Find(process.Name);
            if (entry?.Category == ProcessCategory.Streaming)
            {
                if (process.Priority < PriorityLevel.AboveNormal)
                    changes.Add(new(process.Id, process.Name, process.Priority, PriorityLevel.AboveNormal, "transmissão durante o jogo"));
                continue;
            }
            if (entry?.Category is ProcessCategory.Communication or ProcessCategory.Overlay)
                continue;

            var reason = entry?.LowerDuringGame == true ? $"{entry.Label} em segundo plano"
                : process.CpuPercent >= HogCpuPercent ? $"usando {process.CpuPercent:0}% da CPU durante o jogo"
                : null;
            if (reason is not null && process.Priority > PriorityLevel.BelowNormal)
                changes.Add(new(process.Id, process.Name, process.Priority, PriorityLevel.BelowNormal, reason));
        }
        return changes;
    }
}
