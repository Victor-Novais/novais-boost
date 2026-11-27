using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal static class Program
{
    public static string Version { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        bool Has(string flag) => args.Contains(flag, StringComparer.OrdinalIgnoreCase);

        if (Has("--versao") || Has("--version"))
        {
            Console.WriteLine($"NOVAIS boost {Version}");
            return 0;
        }
        var background = Has("--guardian") || Has("--tarefa");
        if (!background)
            ConsoleUi.DrawBanner();
        if (Has("--ajuda") || Has("--help") || Has("/?"))
        {
            Help();
            return 0;
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            AnsiConsole.MarkupLine("[red]O NOVAIS boost requer Windows 11.[/]");
            return 1;
        }

        IActionLog? log = null;
        string? dataDirectoryForReports = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            var owner = identity.User?.Value ?? throw new InvalidOperationException("Não foi possível identificar a conta do Windows.");

            // Read-only modes: work without elevation; nothing is written.
            if (Has("--diagnostico"))
            {
                var directory = isAdmin ? AppStorage.Open() : null;
                log = directory is null ? new NullLog() : new DailyLog(directory);
                var probe = new WindowsHardwareProbe(log);
                DiagnosticView.Run(probe.Capture, log, directory is null ? null : Path.Combine(directory, "reports"));
                return 0;
            }
            if (Has("--simular"))
            {
                log = new NullLog();
                var preview = AppServices.Create(Path.Combine(Path.GetTempPath(), "NOVAIS boost preview"), log, owner);
                new ConsoleUi(preview).Preview(Has("--extremo") ? TweakLevel.Extreme : TweakLevel.Competitive);
                return 0;
            }
            if (Has("--reparar"))
            {
                log = new NullLog();
                var preview = AppServices.Create(Path.Combine(Path.GetTempPath(), "NOVAIS boost preview"), log, owner);
                RepairView.Draw(RepairView.Scan(preview));
                AnsiConsole.MarkupLine("[grey]Somente leitura: nada foi alterado. Para reparar, abra o menu [[10]] Modo Reparar (pede administrador).[/]");
                return 0;
            }

            if (!isAdmin)
                return background ? 1 : RequestElevation(args);

            var dataDirectory = AppStorage.Open();
            dataDirectoryForReports = dataDirectory;
            log = new DailyLog(dataDirectory);

            // 20.4: runs as SYSTEM at startup; the backup belongs to the account recorded when the guard was armed.
            var taskIndex = Array.FindIndex(args, arg => string.Equals(arg, "--tarefa", StringComparison.OrdinalIgnoreCase));
            if (taskIndex >= 0 && taskIndex + 1 < args.Length && args[taskIndex + 1] == "protecao-boot")
                return BootProtection.OnBoot(dataDirectory, log);

            var app = AppServices.Create(dataDirectory, log, owner);

            if (Has("--voltar-versao"))
            {
                AnsiConsole.MarkupLine(Updater.Rollback(log) ? "[green]Versão anterior restaurada. Abra o NOVAIS boost de novo.[/]"
                    : "[yellow]Não há versão anterior guardada.[/]");
                return 0;
            }
            if (Has("--desinstalar"))
            {
                Uninstaller.Run(app, ConsoleUi.Confirm);
                return 0;
            }
            if (CommandLine.TryRun(args, app) is { } code)
                return code;

            if (Has("--guardian"))
            {
                using var stop = new CancellationTokenSource();
                Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
                new GuardianService(app.GuardianPolicy(), log, echo: !Console.IsOutputRedirected).Run(stop.Token);
                return 0;
            }
            var task = Array.FindIndex(args, arg => string.Equals(arg, "--tarefa", StringComparison.OrdinalIgnoreCase));
            if (task >= 0)
                return task + 1 < args.Length ? MaintenanceTasks.Run(args[task + 1], app) : 1;

            log.Info($"NOVAIS boost {Version} iniciado.");
            if (!FirstRun.EnsureTermsAccepted(app))
                return 0;
            FirstRun.OfferUpdate(app);
            HardwareCard.Draw(log);
            new ConsoleUi(app).Run();
            log.Info("NOVAIS boost encerrado.");
            return 0;
        }
        catch (Exception error)
        {
            try
            {
                log?.Error(error.ToString());
                ErrorReporter.Report(error, dataDirectoryForReports);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                AnsiConsole.MarkupLine("[yellow]Falha ao gravar o log.[/]");
            }
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
            return 1;
        }
    }

    private static void Help()
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Comando").AddColumn("O que faz");
        table.AddRow("(sem argumentos)", "Abre o menu (pede administrador).");
        table.AddRow("--diagnostico", "Diagnóstico completo, somente leitura, sem administrador.");
        table.AddRow("--simular [[--extremo]]", "Mostra o que a otimização mudaria neste PC, sem alterar nada.");
        table.AddRow("--reparar", "Procura alterações de outros otimizadores, sem alterar nada.");
        table.AddRow("--guardian", "Roda o Process Guardian (usado pela tarefa de logon).");
        table.AddRow("--tarefa <id>", "Executa uma tarefa agendada: reaplicar, limpeza, ssd, saude, drivers, rede, protecao-boot, protecao-logon.");
        table.AddRow("--aplicar <<1|2|3>> --confirmar", "Aplica um nível sem perguntas (para testes em VM; sempre com backup).");
        table.AddRow("--reverter-tudo --confirmar", "Desfaz tudo sem perguntas (para testes em VM).");
        table.AddRow("--desinstalar", "Desfaz tudo e remove tarefas, perfis e o plano de energia do NOVAIS boost.");
        table.AddRow("--voltar-versao", "Volta para a versão anterior à última atualização automática.");
        table.AddRow("--versao", "Mostra a versão.");
        AnsiConsole.Write(table);
    }

    private static int RequestElevation(string[] args)
    {
        AnsiConsole.MarkupLine("[yellow]O NOVAIS boost precisa ser executado como administrador.[/]");
        if (Console.IsInputRedirected || !ConsoleUi.Confirm("Reabrir como administrador?"))
            return 1;

        var (command, arguments) = Launcher.Command(string.Join(' ', args.Select(arg => $"\"{arg}\"")), hidden: false);
        var info = new ProcessStartInfo(command.Trim('"')) { UseShellExecute = true, Verb = "runas", Arguments = arguments };
        if (command.Contains("\" \"", StringComparison.Ordinal))
        {
            // dotnet "app.dll": split the host from the dll argument.
            var parts = command.Split("\" \"", 2);
            info.FileName = parts[0].Trim('"');
            info.Arguments = $"\"{parts[1].Trim('"')}\" {arguments}";
        }

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
