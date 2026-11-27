namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Schedules (F6)

public enum ScheduleTrigger { Logon, Daily, Weekly, Monthly, Boot }

/// <param name="Argument">Command line argument passed to NovaisBoost.App.exe.</param>
/// <param name="RunAsSystem">Runs as SYSTEM (boot protection, before anyone logs on).</param>
public sealed record ScheduleDefinition(
    string Id, string Name, string Description, ScheduleTrigger Trigger, DayOfWeek? Day, int? DayOfMonth, TimeOnly? Time, string Argument,
    bool RunAsSystem = false);

public static class Schedules
{
    public const string Folder = @"\NOVAIS boost\";

    public static readonly IReadOnlyList<ScheduleDefinition> All =
    [
        new("guardian", "Process Guardian", "Inicia o Process Guardian ao entrar no Windows (prioridades automáticas durante o jogo).",
            ScheduleTrigger.Logon, null, null, null, "--guardian"),
        new("reaplicar", "Reaplicar otimizações", "Todo dia: reaplica ajustes que o Windows Update desfez e limpa o cache de shader quando o driver da GPU muda.",
            ScheduleTrigger.Daily, null, null, new TimeOnly(12, 0), "--tarefa reaplicar"),
        new("limpeza", "Limpeza semanal", "Toda sexta: apaga temporários, despejos de erro e logs antigos.",
            ScheduleTrigger.Weekly, DayOfWeek.Friday, null, new TimeOnly(20, 0), "--tarefa limpeza"),
        new("ssd", "Otimização do SSD", "Todo domingo: TRIM nos SSDs e verificação de saúde dos discos.",
            ScheduleTrigger.Weekly, DayOfWeek.Sunday, null, new TimeOnly(15, 0), "--tarefa ssd"),
        new("saude", "Saúde mensal", "Dia 1 de cada mês: diagnóstico salvo em relatório, reparo da imagem do Windows (DISM /RestoreHealth e sfc /scannow), limpeza de componentes e ponto de restauração.",
            ScheduleTrigger.Monthly, null, 1, new TimeOnly(19, 0), "--tarefa saude"),
        new("drivers", "Aviso de driver antigo", "Toda quarta: avisa (sem instalar nada) quando o driver da placa de vídeo passou de 6 meses.",
            ScheduleTrigger.Weekly, DayOfWeek.Wednesday, null, new TimeOnly(19, 0), "--tarefa drivers"),
        new("rede", "Teste de rede semanal", "Toda quinta: mede ping, jitter e DNS. Se você já usa o \"DNS mais rápido\" e outro ficou 10 ms mais rápido, troca para ele.",
            ScheduleTrigger.Weekly, DayOfWeek.Thursday, null, new TimeOnly(21, 0), "--tarefa rede")
    ];

    /// <summary>20.4: armed only after changes that need a restart; not in the user's list of schedules.</summary>
    public static readonly ScheduleDefinition BootGuard = new("protecao-boot", "Proteção depois do reinício",
        "Conta os reinícios depois de ajustes que pedem reinício. Se o Windows não chegar à área de trabalho 2 vezes seguidas, desfaz tudo.",
        ScheduleTrigger.Boot, null, null, null, "--tarefa protecao-boot", RunAsSystem: true);

    public static readonly ScheduleDefinition BootConfirm = new("protecao-logon", "Confirmação de reinício saudável",
        "Ao entrar no Windows, confirma que o PC iniciou bem e desliga a proteção depois do reinício.",
        ScheduleTrigger.Logon, null, null, null, "--tarefa protecao-logon");

    public static ScheduleDefinition Find(string id) =>
        All.FirstOrDefault(schedule => schedule.Id == id) ?? throw new ArgumentException($"Tarefa desconhecida: {id}.", nameof(id));
}

// ---------------------------------------------------------------- Cleanup (F6)

/// <summary>Regular runs every week; ShaderCache after a driver change; Browser and Launcher only with opt-in in settings.json.</summary>
public enum CleanupKind { Regular, ShaderCache, Browser, Launcher }

/// <param name="Path">May contain environment variables such as %TEMP%.</param>
/// <param name="MinAgeDays">Only files older than this are deleted (files in use are always skipped).</param>
/// <param name="Pattern">File name pattern inside <paramref name="Path"/>.</param>
/// <param name="Recursive">Also walk sub-folders (and remove the ones left empty).</param>
/// <param name="LockedBy">Process names (without .exe): nothing is deleted while one of them runs.</param>
public sealed record CleanupTarget(string Name, string Path, int MinAgeDays, CleanupKind Kind = CleanupKind.Regular,
    string Pattern = "*", bool Recursive = true, string[]? LockedBy = null);

public static class CleanupTargets
{
    public static readonly IReadOnlyList<CleanupTarget> All =
    [
        new("Temporários do usuário", "%TEMP%", 2),
        new("Temporários do Windows", @"%SystemRoot%\Temp", 2),
        new("Despejos de falha de apps", @"%LOCALAPPDATA%\CrashDumps", 0),
        new("Minidumps antigos", @"%SystemRoot%\Minidump", 14),
        new("Relatórios de erro (arquivo)", @"%ProgramData%\Microsoft\Windows\WER\ReportArchive", 7),
        new("Relatórios de erro (fila)", @"%ProgramData%\Microsoft\Windows\WER\ReportQueue", 7),
        new("Logs da Steam", @"%ProgramFiles(x86)%\Steam\logs", 7),
        new("Despejos da Steam", @"%ProgramFiles(x86)%\Steam\dumps", 0),
        new("Logs da Epic Games", @"%LOCALAPPDATA%\EpicGamesLauncher\Saved\Logs", 7),
        new("Cache de miniaturas", @"%LOCALAPPDATA%\Microsoft\Windows\Explorer", 0, Pattern: "thumbcache_*.db", Recursive: false),
        new("Cache do Chrome", @"%LOCALAPPDATA%\Google\Chrome\User Data\Default\Cache\Cache_Data", 0, CleanupKind.Browser, LockedBy: ["chrome"]),
        new("Cache do Edge", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Default\Cache\Cache_Data", 0, CleanupKind.Browser, LockedBy: ["msedge"]),
        new("Cache do Brave", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\Default\Cache\Cache_Data", 0, CleanupKind.Browser, LockedBy: ["brave"]),
        new("Cache web da Steam", @"%LOCALAPPDATA%\Steam\htmlcache", 0, CleanupKind.Launcher, LockedBy: ["steam", "steamwebhelper"]),
        new("Cache do Battle.net", @"%ProgramData%\Blizzard Entertainment\Battle.net\Cache", 0, CleanupKind.Launcher, LockedBy: ["Battle.net", "Agent"]),
        new("Cache de shader DirectX", @"%LOCALAPPDATA%\D3DSCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader NVIDIA (DX)", @"%LOCALAPPDATA%\NVIDIA\DXCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader NVIDIA (GL)", @"%LOCALAPPDATA%\NVIDIA\GLCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader AMD", @"%LOCALAPPDATA%\AMD\DxCache", 0, CleanupKind.ShaderCache)
    ];

    /// <summary>The Recycle Bin keeps items deleted for more than this many days only until the next weekly cleanup.</summary>
    public const int RecycleBinDays = 30;

    /// <summary>Targets a run uses: regular ones plus the opt-in browser and launcher caches.</summary>
    public static IReadOnlyList<CleanupTarget> Weekly(CleanupSettings settings) => All.Where(target => target.Kind switch
    {
        CleanupKind.Regular => true,
        CleanupKind.Browser => settings.Browsers,
        CleanupKind.Launcher => settings.Launchers,
        _ => false
    }).ToArray();

    /// <summary>True when a file of this age should be removed under the target's rule.</summary>
    public static bool IsExpired(CleanupTarget target, DateTime lastWriteUtc, DateTime nowUtc) =>
        nowUtc - lastWriteUtc >= TimeSpan.FromDays(target.MinAgeDays);
}
