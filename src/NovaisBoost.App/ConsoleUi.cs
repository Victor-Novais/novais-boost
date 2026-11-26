using System.Diagnostics;
using NovaisBoost.Core;
using Spectre.Console;

namespace NovaisBoost.App;

internal sealed class ConsoleUi(
    TweakEngine engine, SnapshotManager snapshots, IReadOnlyList<TweakDefinition> tweaks, IActionLog log, Action diagnose)
{
    private static readonly string[] Brand =
    [
        "███╗   ██╗ ██████╗ ██╗   ██╗ █████╗ ██╗███████╗",
        "████╗  ██║██╔═══██╗██║   ██║██╔══██╗██║██╔════╝",
        "██╔██╗ ██║██║   ██║██║   ██║███████║██║███████╗",
        "██║╚██╗██║██║   ██║╚██╗ ██╔╝██╔══██║██║╚════██║",
        "██║ ╚████║╚██████╔╝ ╚████╔╝ ██║  ██║██║███████║",
        "╚═╝  ╚═══╝ ╚═════╝   ╚═══╝  ╚═╝  ╚═╝╚═╝╚══════╝"
    ];

    private static readonly string[] Boost =
    [
        "██████╗  ██████╗  ██████╗ ███████╗████████╗",
        "██╔══██╗██╔═══██╗██╔═══██╗██╔════╝╚══██╔══╝",
        "██████╔╝██║   ██║██║   ██║███████╗   ██║",
        "██╔══██╗██║   ██║██║   ██║╚════██║   ██║",
        "██████╔╝╚██████╔╝╚██████╔╝███████║   ██║",
        "╚═════╝  ╚═════╝  ╚═════╝ ╚══════╝   ╚═╝"
    ];

    private static readonly Color[] Gradient =
    [
        new(124, 58, 237), new(142, 69, 241), new(159, 80, 245),
        new(168, 85, 247), new(182, 113, 250), new(192, 132, 252)
    ];

    private static readonly string[] Menu =
    [
        "Diagnóstico completo", "Otimização Universal", "Perfil do meu Hardware", "Jogos & Streaming",
        "Process Guardian", "Cronogramas", "Benchmark antes/depois", "Reverter / Backups"
    ];

    public static void DrawBanner()
    {
        AnsiConsole.WriteLine();
        if (AnsiConsole.Profile.Width >= 50)
        {
            for (var row = 0; row < Brand.Length; row++)
                AnsiConsole.Write(new Text("  " + Brand[row] + Environment.NewLine, new Style(Gradient[row], decoration: Decoration.Bold)));
            foreach (var row in Boost)
                AnsiConsole.Write(new Text("       " + row + Environment.NewLine, new Style(Color.White, decoration: Decoration.Bold)));
        }
        else
        {
            AnsiConsole.MarkupLine("[bold #A855F7]NOVAIS[/] [bold white]boost[/]");
        }

        AnsiConsole.Write(new Rule().RuleStyle("#A855F7"));
        AnsiConsole.MarkupLine("[grey]Mais FPS · Menos latência · Otimizador para Windows 11[/]");
        AnsiConsole.Write(new Rule().RuleStyle("#A855F7"));
        AnsiConsole.WriteLine();
    }

    public void Run()
    {
        while (true)
        {
            DrawMenu();
            if (Console.IsInputRedirected)
                return;

            var input = ReadChoice();
            if (input is null or "0")
                return;
            if (!int.TryParse(input, out var choice) || choice < 1 || choice > Menu.Length)
            {
                AnsiConsole.MarkupLine("[yellow]Escolha uma opção de 0 a 8.[/]");
                continue;
            }

            log.Info($"Menu: {Menu[choice - 1]}.");
            RunSafely(() =>
            {
                switch (choice)
                {
                    case 1:
                        diagnose();
                        break;
                    case 2:
                        AnsiConsole.MarkupLine("[grey]Otimização Universal: Em breve.[/]");
                        ShowExamples();
                        break;
                    case 8:
                        ShowBackups();
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Menu[choice - 1])}: Em breve.[/]");
                        break;
                }
            });
            AnsiConsole.WriteLine();
        }
    }

    private static void DrawMenu()
    {
        if (AnsiConsole.Profile.Width >= 64)
        {
            var grid = new Grid().AddColumn().AddColumn();
            for (var index = 0; index < 4; index++)
                grid.AddRow(MenuItem(index + 1, Menu[index]), MenuItem(index + 5, Menu[index + 4]));
            AnsiConsole.Write(grid);
        }
        else
        {
            for (var index = 0; index < Menu.Length; index++)
                AnsiConsole.MarkupLine(MenuItem(index + 1, Menu[index]));
        }
        AnsiConsole.MarkupLine(MenuItem(0, "Sair"));
    }

    private void ShowExamples()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Ajustes de exemplo[/]");
        for (var index = 0; index < tweaks.Count; index++)
            AnsiConsole.MarkupLine(MenuItem(index + 1, tweaks[index].Name));
        AnsiConsole.MarkupLine(MenuItem(0, "Voltar"));
        var input = ReadChoice();
        if (input is null or "0")
            return;
        if (!int.TryParse(input, out var selection) || selection < 1 || selection > tweaks.Count)
        {
            AnsiConsole.MarkupLine("[yellow]Opção inválida.[/]");
            return;
        }

        var tweak = tweaks[selection - 1];
        AnsiConsole.WriteLine(tweak.Description);
        AnsiConsole.WriteLine(tweak.Risk);
        var changes = engine.Apply(tweak);
        if (changes.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]O ajuste já está aplicado.[/]");
            return;
        }

        DrawPreview(changes);
        if (!Confirm("Aplicar este ajuste com backup?"))
        {
            log.Info($"Aplicação cancelada: {tweak.Id}.");
            return;
        }
        engine.Apply(tweak, dryRun: false, reviewedChanges: changes);
        AnsiConsole.MarkupLine("[green]Ajuste aplicado.[/]");
    }

    private void ShowBackups()
    {
        var active = snapshots.GetActive();
        if (active is null)
        {
            AnsiConsole.MarkupLine("[grey]Nenhum backup ativo para esta conta.[/]");
            return;
        }

        AnsiConsole.WriteLine($"Backup ativo: {active.Path}");
        AnsiConsole.WriteLine($"Ajustes registrados: {active.TweakCount} · Ponto de restauração: {active.RestorePointNumber}");
        if (active.Pending)
            AnsiConsole.MarkupLine("[yellow]Há uma operação pendente neste backup.[/]");
        AnsiConsole.MarkupLine(MenuItem(1, "Reverter tudo"));
        AnsiConsole.MarkupLine(MenuItem(0, "Voltar"));
        if (ReadChoice() != "1")
            return;

        var changes = engine.RevertAll();
        DrawPreview(changes);
        if (!Confirm("Reverter tudo e encerrar o backup ativo?"))
        {
            log.Info("Reversão cancelada.");
            return;
        }
        engine.RevertAll(dryRun: false, reviewedChanges: changes);
        AnsiConsole.MarkupLine("[green]Reversão concluída. Reabra a sessão do Windows se algum efeito persistir.[/]");
    }

    private void RunSafely(Action operation)
    {
        try
        {
            operation();
        }
        catch (SystemProtectionDisabledException error)
        {
            log.Warning(error.Message);
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(error.Message)}[/]");
            if (!Confirm("Abrir a Proteção do Sistema para ativá-la?"))
                return;

            try
            {
                var path = Path.Combine(Environment.SystemDirectory, "SystemPropertiesProtection.exe");
                using var settings = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                log.Info("Configuração da Proteção do Sistema aberta.");
            }
            catch (Exception launchError)
            {
                ShowError(launchError);
            }
        }
        catch (Exception error)
        {
            ShowError(error);
        }
    }

    private void ShowError(Exception error)
    {
        log.Error(error.ToString());
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error.Message)}[/]");
    }

    private static void DrawPreview(IReadOnlyList<SettingChange> changes)
    {
        AnsiConsole.MarkupLine("[bold #C084FC]Simulação[/]");
        if (changes.Count == 0)
            AnsiConsole.WriteLine("O estado já corresponde ao backup.");
        foreach (var change in changes)
            AnsiConsole.WriteLine($"  {change.Target}{Environment.NewLine}  {change.Current} → {change.Expected}");
    }

    private static string MenuItem(int index, string name) =>
        $"[bold #C084FC][[{index}]][/] {Markup.Escape(name)}";

    private static string? ReadChoice()
    {
        AnsiConsole.Write("Opção: ");
        return Console.ReadLine()?.Trim();
    }

    internal static bool Confirm(string question)
    {
        AnsiConsole.Write($"{question} [s/N] ");
        var answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(answer, "sim", StringComparison.OrdinalIgnoreCase);
    }
}
