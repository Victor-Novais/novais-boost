namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Schedules (F6)

public enum ScheduleTrigger { Logon, Daily, Weekly, Monthly }

/// <param name="Argument">Command line argument passed to NovaisBoost.App.exe.</param>
public sealed record ScheduleDefinition(
    string Id, string Name, string Description, ScheduleTrigger Trigger, DayOfWeek? Day, int? DayOfMonth, TimeOnly? Time, string Argument);

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
        new("saude", "Saúde mensal", "Dia 1 de cada mês: diagnóstico completo salvo em relatório, limpeza de componentes do Windows (DISM) e ponto de restauração.",
            ScheduleTrigger.Monthly, null, 1, new TimeOnly(19, 0), "--tarefa saude")
    ];

    public static ScheduleDefinition Find(string id) =>
        All.FirstOrDefault(schedule => schedule.Id == id) ?? throw new ArgumentException($"Tarefa desconhecida: {id}.", nameof(id));
}

// ---------------------------------------------------------------- Cleanup (F6)

public enum CleanupKind { Regular, ShaderCache }

/// <param name="Path">May contain environment variables such as %TEMP%.</param>
/// <param name="MinAgeDays">Only files older than this are deleted (files in use are always skipped).</param>
public sealed record CleanupTarget(string Name, string Path, int MinAgeDays, CleanupKind Kind = CleanupKind.Regular);

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
        new("Cache de shader DirectX", @"%LOCALAPPDATA%\D3DSCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader NVIDIA (DX)", @"%LOCALAPPDATA%\NVIDIA\DXCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader NVIDIA (GL)", @"%LOCALAPPDATA%\NVIDIA\GLCache", 0, CleanupKind.ShaderCache),
        new("Cache de shader AMD", @"%LOCALAPPDATA%\AMD\DxCache", 0, CleanupKind.ShaderCache)
    ];

    /// <summary>True when a file of this age should be removed under the target's rule.</summary>
    public static bool IsExpired(CleanupTarget target, DateTime lastWriteUtc, DateTime nowUtc) =>
        nowUtc - lastWriteUtc >= TimeSpan.FromDays(target.MinAgeDays);
}
