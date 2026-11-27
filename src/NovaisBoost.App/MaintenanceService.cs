using System.ComponentModel;
using System.Globalization;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>Registers the NOVAIS boost tasks in the Windows Task Scheduler (\NOVAIS boost\...).</summary>
internal static class TaskSchedulerService
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static bool IsRegistered(ScheduleDefinition schedule)
    {
        try
        {
            WindowsCommand.Run("schtasks.exe", "/query", "/tn", Schedules.Folder + schedule.Id);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    public static void Register(ScheduleDefinition schedule)
    {
        var file = Path.Combine(Path.GetTempPath(), $"novais-{schedule.Id}-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, BuildXml(schedule).ToString(), Encoding.Unicode);
            WindowsCommand.Run("schtasks.exe", "/create", "/tn", Schedules.Folder + schedule.Id, "/xml", file, "/f");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public static void Remove(ScheduleDefinition schedule) =>
        WindowsCommand.Run("schtasks.exe", "/delete", "/tn", Schedules.Folder + schedule.Id, "/f");

    public static void RunNow(ScheduleDefinition schedule) =>
        WindowsCommand.Run("schtasks.exe", "/run", "/tn", Schedules.Folder + schedule.Id);

    internal static XDocument BuildXml(ScheduleDefinition schedule)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Conta do Windows não identificada.");
        var start = new DateTime(2026, 1, 1).Add((schedule.Time ?? new TimeOnly(12, 0)).ToTimeSpan()).ToString("s", CultureInfo.InvariantCulture);

        XElement trigger = schedule.Trigger switch
        {
            ScheduleTrigger.Boot => new XElement(Ns + "BootTrigger", new XElement(Ns + "Enabled", "true"), new XElement(Ns + "Delay", "PT45S")),
            ScheduleTrigger.Logon => new XElement(Ns + "LogonTrigger", new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "UserId", sid), new XElement(Ns + "Delay", "PT30S")),
            ScheduleTrigger.Daily => Calendar(start, new XElement(Ns + "ScheduleByDay", new XElement(Ns + "DaysInterval", 1))),
            ScheduleTrigger.Weekly => Calendar(start, new XElement(Ns + "ScheduleByWeek", new XElement(Ns + "WeeksInterval", 1),
                new XElement(Ns + "DaysOfWeek", new XElement(Ns + (schedule.Day ?? DayOfWeek.Friday).ToString())))),
            _ => Calendar(start, new XElement(Ns + "ScheduleByMonth",
                new XElement(Ns + "DaysOfMonth", new XElement(Ns + "Day", schedule.DayOfMonth ?? 1)),
                new XElement(Ns + "Months", CultureInfo.InvariantCulture.DateTimeFormat.MonthNames.Where(name => name.Length > 0)
                    .Select(name => new XElement(Ns + name)))))
        };

        var (command, arguments) = Launcher.Command(schedule.Argument, hidden: true);
        return new XDocument(new XElement(Ns + "Task", new XAttribute("version", "1.4"),
            new XElement(Ns + "RegistrationInfo", new XElement(Ns + "Author", "NOVAIS boost"), new XElement(Ns + "Description", schedule.Description)),
            new XElement(Ns + "Triggers", trigger),
            new XElement(Ns + "Principals", schedule.RunAsSystem
                ? new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", "S-1-5-18"), new XElement(Ns + "RunLevel", "HighestAvailable"))
                : new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", sid), new XElement(Ns + "LogonType", "InteractiveToken"), new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "StartWhenAvailable", "true"),
                new XElement(Ns + "ExecutionTimeLimit", schedule.Trigger == ScheduleTrigger.Logon && schedule.Id == "guardian" ? "PT0S" : "PT2H"),
                new XElement(Ns + "Priority", 7),
                new XElement(Ns + "Enabled", "true")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec", new XElement(Ns + "Command", command), new XElement(Ns + "Arguments", arguments)))));

        XElement Calendar(string boundary, XElement schedule) =>
            new(Ns + "CalendarTrigger", new XElement(Ns + "StartBoundary", boundary), new XElement(Ns + "Enabled", "true"), schedule);
    }
}

internal static class Launcher
{
    /// <summary>How to start this program again (published exe, or "dotnet app.dll" in development).</summary>
    public static (string Command, string Arguments) Command(string arguments, bool hidden)
    {
        var process = Environment.ProcessPath ?? throw new InvalidOperationException("Executável não encontrado.");
        var target = string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{process}\" \"{Path.Combine(AppContext.BaseDirectory, "NovaisBoost.App.dll")}\""
            : $"\"{process}\"";
        // conhost --headless runs the console program without opening a window.
        return hidden
            ? (Path.Combine(Environment.SystemDirectory, "conhost.exe"), $"--headless {target} {arguments}")
            : (target, arguments);
    }
}

internal sealed record CleanupResult(CleanupTarget Target, int Files, long Bytes, int Skipped);

internal static class Cleaner
{
    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    public static IReadOnlyList<CleanupResult> Run(IEnumerable<CleanupTarget> targets, bool dryRun, IActionLog log)
    {
        var results = new List<CleanupResult>();
        var now = DateTime.UtcNow;
        foreach (var target in targets)
        {
            var path = Environment.ExpandEnvironmentVariables(target.Path);
            if (path.Contains('%') || !Directory.Exists(path) || !IsSafe(path))
                continue;
            if (target.LockedBy?.FirstOrDefault(IsRunning) is { } running)
            {
                log.Info($"Limpeza: {target.Name} ignorado porque {running} está aberto.");
                continue;
            }

            int files = 0, skipped = 0;
            long bytes = 0;
            var walk = target.Recursive ? Walk : new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = Walk.AttributesToSkip };
            foreach (var file in Directory.EnumerateFiles(path, target.Pattern, walk))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (!CleanupTargets.IsExpired(target, info.LastWriteTimeUtc, now))
                        continue;
                    var length = info.Length;
                    if (!dryRun)
                        info.Delete();
                    files++;
                    bytes += length;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    skipped++; // in use or protected
                }
            }
            if (!dryRun && target.Recursive)
                RemoveEmptyFolders(path);
            results.Add(new CleanupResult(target, files, bytes, skipped));
            if (!dryRun && files > 0)
                log.Info($"Limpeza: {target.Name}: {files} arquivo(s), {Core.Diagnostics.Gb(bytes)}.");
        }
        return results;
    }

    private static bool IsRunning(string name) => System.Diagnostics.Process.GetProcessesByName(name) is { Length: > 0 } found && Dispose(found);

    private static bool Dispose(System.Diagnostics.Process[] processes)
    {
        foreach (var process in processes)
            process.Dispose();
        return true;
    }

    /// <summary>
    /// 23.1: items deleted more than 30 days ago, read from the "$I" file of each item in this user's
    /// \$Recycle.Bin\&lt;SID&gt; folder on every fixed drive. Recent items are never touched.
    /// </summary>
    public static CleanupResult CleanRecycleBin(bool dryRun, IActionLog log)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        var target = new CleanupTarget($"Lixeira (itens com mais de {CleanupTargets.RecycleBinDays} dias)", "$Recycle.Bin", CleanupTargets.RecycleBinDays);
        int files = 0, skipped = 0;
        long bytes = 0;
        if (sid is null)
            return new CleanupResult(target, 0, 0, 0);
        var now = DateTime.UtcNow;
        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            var folder = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
            if (!Directory.Exists(folder))
                continue;
            foreach (var info in Directory.EnumerateFiles(folder, "$I*"))
            {
                try
                {
                    if (RecycleBin.DeletedAtUtc(File.ReadAllBytes(info)) is not { } deleted || !RecycleBin.IsExpired(deleted, now) ||
                        RecycleBin.ContentName(Path.GetFileName(info)) is not { } contentName)
                        continue;
                    var content = Path.Combine(folder, contentName);
                    var size = File.Exists(content) ? new FileInfo(content).Length
                        : Directory.Exists(content) ? Directory.EnumerateFiles(content, "*", Walk).Sum(file => new FileInfo(file).Length) : 0;
                    if (!dryRun)
                    {
                        if (File.Exists(content))
                            File.Delete(content);
                        else if (Directory.Exists(content))
                            Directory.Delete(content, recursive: true);
                        File.Delete(info);
                    }
                    files++;
                    bytes += size;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                }
            }
        }
        if (!dryRun && files > 0)
            log.Info($"Limpeza: Lixeira: {files} item(ns) com mais de {CleanupTargets.RecycleBinDays} dias, {Core.Diagnostics.Gb(bytes)}.");
        return new CleanupResult(target, files, bytes, skipped);
    }

    /// <summary>23.1: Delivery Optimization cache, through its own supported cmdlet.</summary>
    public static void CleanDeliveryOptimization(IActionLog log)
    {
        try
        {
            WindowsCommand.Run(TimeSpan.FromMinutes(5), Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile", "-NonInteractive", "-Command", "Delete-DeliveryOptimizationCache -Force -ErrorAction Stop");
            log.Info("Limpeza: cache da Otimização de Entrega removido.");
        }
        catch (Exception error) when (error is Win32Exception or TimeoutException)
        {
            log.Warning($"Limpeza: cache da Otimização de Entrega não removido: {error.Message}");
        }
    }

    /// <summary>Refuses drive roots and top-level system folders, whatever the configuration says.</summary>
    private static bool IsSafe(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        string[] forbidden =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        ];
        return full.Count(character => character == '\\') >= 2 &&
            !forbidden.Any(folder => folder.Length > 0 && string.Equals(full, folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    private static void RemoveEmptyFolders(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root, "*", Walk).OrderByDescending(folder => folder.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // In use; keep it.
            }
        }
    }
}

/// <summary>What each scheduled task does when Windows starts "NovaisBoost.App.exe --tarefa &lt;id&gt;".</summary>
internal static class MaintenanceTasks
{
    public static int Run(string id, AppServices app)
    {
        var log = app.Log;
        log.Info($"Tarefa agendada iniciada: {id}.");
        try
        {
            switch (id)
            {
                case "reaplicar":
                    Reapply(app);
                    CheckDriverChange(app);
                    break;
                case "limpeza":
                    Cleaner.Run(CleanupTargets.Weekly(app.Settings.CleanupOrDefault), dryRun: false, log);
                    Cleaner.CleanRecycleBin(dryRun: false, log);
                    Cleaner.CleanDeliveryOptimization(log);
                    break;
                case "ssd":
                    WindowsCommand.Run(TimeSpan.FromMinutes(60), "defrag.exe", "/C", "/O", "/H");
                    foreach (var disk in app.Pc.Disks.Where(disk => disk.Health is DiskHealth.Warning or DiskHealth.Unhealthy))
                        log.Warning($"Disco com problema de saúde: {disk.Name}. Faça backup.");
                    break;
                case "saude":
                    var report = Core.Diagnostics.Evaluate(app.Pc, DateOnly.FromDateTime(DateTime.Now), app.Settings.PcOrDefault);
                    ReportStore.SaveDiagnostic(app, app.Pc, report);
                    Integrity(log);
                    WindowsCommand.Run(TimeSpan.FromMinutes(90), "Dism.exe", "/Online", "/Cleanup-Image", "/StartComponentCleanup");
                    new WindowsRestorePoints().Ensure();
                    break;
                case "drivers":
                    foreach (var warning in DriverCheck.Outdated(app.Pc, DateOnly.FromDateTime(DateTime.Now)))
                    {
                        log.Warning($"Driver antigo: {warning}");
                        Notifier.Show("NOVAIS boost · Driver de vídeo antigo", warning, log);
                    }
                    break;
                case "rede":
                    WeeklyNetwork(app);
                    break;
                case "protecao-logon":
                    return BootProtection.OnLogon(app);
                default:
                    throw new ArgumentException($"Tarefa desconhecida: {id}.");
            }
            log.Info($"Tarefa agendada concluída: {id}.");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            log.Error($"Tarefa agendada {id} falhou: {error}");
            return 1;
        }
    }

    /// <summary>23.2: DISM repairs the component store first, then sfc checks the system files against it.</summary>
    private static void Integrity(IActionLog log)
    {
        foreach (var (name, executable, arguments) in new[]
        {
            ("DISM /RestoreHealth", "Dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }),
            ("sfc /scannow", "sfc.exe", new[] { "/scannow" })
        })
        {
            try
            {
                WindowsCommand.Run(TimeSpan.FromMinutes(60), executable, arguments);
                log.Info($"Saúde: {name} concluído sem erros.");
            }
            catch (Exception error) when (error is Win32Exception or TimeoutException)
            {
                // sfc returns non-zero when it found (and possibly repaired) files; the details are in CBS.log.
                log.Warning($"Saúde: {name} terminou com aviso ({error.Message}). Detalhes em %SystemRoot%\\Logs\\CBS\\CBS.log.");
            }
        }
    }

    /// <summary>
    /// 23.4: ping/jitter to the log, and a DNS switch only for someone who already applied "DNS mais rápido"
    /// and when another public resolver is at least 10 ms faster. The switch goes through the engine (backup, revert).
    /// </summary>
    private static void WeeklyNetwork(AppServices app)
    {
        var log = app.Log;
        var ping = NetworkTools.Ping("1.1.1.1", 30, 200);
        log.Info($"Rede semanal: ping {ping.AverageMs:0.0} ms, jitter {ping.JitterMs:0.0} ms, perda {ping.LossPercent:0.#}%.");
        if (app.Snapshots.GetActive() is not { Pending: false } active || !active.TweakIds.Any(id => id.StartsWith("dns-", StringComparison.Ordinal)))
            return;
        var adapter = NetworkTools.ActiveAdapter();
        if (adapter is null || !Guid.TryParse(adapter.Id, out var id))
            return;
        var current = adapter.GetIPProperties().DnsAddresses
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(address => address.ToString()).ToArray();
        var results = NetworkTools.BenchmarkDns(adapter);
        if (Core.DnsAutoSwitch.Choose(results, current) is not { } best)
        {
            log.Info("Rede semanal: o DNS atual continua entre os mais rápidos.");
            return;
        }
        var resolver = NetworkTools.PublicResolvers.FirstOrDefault(item => item.Primary == best.Server);
        if (resolver.Primary is null)
            return;
        var tweak = NetworkTools.DnsTweak(id, adapter.Name, resolver);
        app.Engine.Apply(tweak, dryRun: false);
        log.Warning($"Rede semanal: DNS trocado para {resolver.Name} ({best.MedianMs:0.0} ms). Reverter tudo volta ao DNS original.");
        Notifier.Show("NOVAIS boost · DNS", $"Trocamos o DNS para {resolver.Name}, que ficou mais rápido no teste desta semana.", log);
    }

    /// <summary>Re-applies active tweaks that drifted (usually reverted by a Windows feature update).</summary>
    public static int Reapply(AppServices app)
    {
        var active = app.Snapshots.GetActive();
        if (active is null || active.Pending)
            return 0;
        var catalog = app.AllTweaks.ToDictionary(tweak => tweak.Id, StringComparer.Ordinal);
        var reapplied = 0;
        foreach (var id in active.TweakIds)
        {
            if (!catalog.TryGetValue(id, out var tweak))
            {
                // Removed or renamed id (e.g. "mmcss-gaming" before F11): never reapplied, still undone by Reverter tudo.
                app.Log.Info($"Ajuste {id} não existe mais no catálogo; não é reaplicado, mas continua no backup para reverter.");
                continue;
            }
            if (!TweakLevels.IsProven(tweak, app.LocalEvidence) || app.Engine.CheckCompatibility(tweak) is not null)
                continue;
            if (app.Engine.Detect(tweak).Count == 0)
                continue;
            app.Engine.Apply(tweak, dryRun: false);
            reapplied++;
            app.Log.Warning($"Ajuste {id} tinha sido desfeito pelo Windows e foi reaplicado.");
        }
        return reapplied;
    }

    private static void CheckDriverChange(AppServices app)
    {
        var file = Path.Combine(app.DataDirectory, "gpu-driver.txt");
        var current = string.Join(";", app.Pc.Gpus.Select(gpu => $"{gpu.Name}={gpu.DriverVersion}"));
        var previous = File.Exists(file) ? File.ReadAllText(file) : null;
        if (previous is not null && previous != current)
        {
            Cleaner.Run(CleanupTargets.All.Where(target => target.Kind == CleanupKind.ShaderCache), dryRun: false, app.Log);
            app.Log.Info("Driver de vídeo mudou: cache de shader antigo removido (os jogos recompilam na primeira partida).");
        }
        File.WriteAllText(file, current);
    }
}
