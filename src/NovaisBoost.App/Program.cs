using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        ConsoleUi.DrawBanner();
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            AnsiConsole.MarkupLine("[red]O NOVAIS boost requer Windows 11.[/]");
            return 1;
        }

        IActionLog? log = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

            // Read-only diagnosis; works without elevation (no log or report file in that case).
            if (args.Contains("--diagnostico", StringComparer.OrdinalIgnoreCase))
            {
                var directory = isAdmin ? AppStorage.Open() : null;
                log = directory is null ? new NullLog() : new DailyLog(directory);
                DiagnosticView.Run(new WindowsHardwareProbe(log), log, directory is null ? null : Path.Combine(directory, "reports"));
                return 0;
            }

            // Read-only preview of the universal optimization; never writes (dry-run only).
            if (args.Contains("--simular", StringComparer.OrdinalIgnoreCase))
            {
                log = new NullLog();
                var previewProbe = new WindowsHardwareProbe(log);
                HardwareSnapshot? previewPc = null;
                var previewSnapshots = new SnapshotManager(Path.Combine(Path.GetTempPath(), "NOVAIS boost preview"), identity.User?.Value ?? "preview",
                    new WindowsRestorePoints(), log);
                var previewEngine = new TweakEngine(new WindowsRegistry(), new WindowsServiceController(), new WindowsPowerConfig(), previewSnapshots, log,
                    new WindowsScheduledTasks(log), new WindowsPowerPlans(), compat => Compatibility.Check(compat, previewPc ??= previewProbe.Capture()));
                new ConsoleUi(previewEngine, previewSnapshots, TweakCatalog.LoadEmbedded(), log, () => { }).Preview(TweakLevel.Competitive);
                return 0;
            }

            if (!isAdmin)
                return RequestElevation();

            var dataDirectory = AppStorage.Open();
            log = new DailyLog(dataDirectory);
            log.Info("NOVAIS boost iniciado.");
            var owner = identity.User?.Value ?? throw new InvalidOperationException("Não foi possível identificar a conta do Windows.");
            var snapshots = new SnapshotManager(dataDirectory, owner, new WindowsRestorePoints(), log);
            var probe = new WindowsHardwareProbe(log);
            HardwareSnapshot? pc = null;
            var engine = new TweakEngine(new WindowsRegistry(), new WindowsServiceController(), new WindowsPowerConfig(), snapshots, log,
                new WindowsScheduledTasks(log), new WindowsPowerPlans(),
                compat => Compatibility.Check(compat, pc ??= probe.Capture()));
            HardwareCard.Draw(log);
            var reports = Path.Combine(dataDirectory, "reports");
            new ConsoleUi(engine, snapshots, TweakCatalog.LoadEmbedded(), log,
                () => DiagnosticView.Run(probe, log, reports)).Run();
            log.Info("NOVAIS boost encerrado.");
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                log?.Error(error.ToString());
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine("[yellow]Falha ao gravar o log.[/]");
            }
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
            return 1;
        }
    }

    private static int RequestElevation()
    {
        AnsiConsole.MarkupLine("[yellow]O NOVAIS boost precisa ser executado como administrador.[/]");
        if (Console.IsInputRedirected || !ConsoleUi.Confirm("Reabrir como administrador?"))
            return 1;

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Não foi possível localizar o executável.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));

        try
        {
            using var process = Process.Start(info);
            return 0;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            AnsiConsole.MarkupLine("[yellow]A solicitação de administrador foi cancelada.[/]");
            return 1;
        }
    }
}
