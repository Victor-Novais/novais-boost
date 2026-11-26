using System.Diagnostics;
using System.Globalization;
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
                        ShowUniversal();
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

    private void ShowUniversal()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Otimização Universal[/]");
        AnsiConsole.MarkupLine(MenuItem(1, "Nível 1 · Seguro — para qualquer PC"));
        AnsiConsole.MarkupLine(MenuItem(2, "Nível 2 · Competitivo — inclui o nível 1"));
        AnsiConsole.MarkupLine(MenuItem(3, "Ajustes individuais"));
        AnsiConsole.MarkupLine(MenuItem(0, "Voltar"));
        switch (ReadChoice())
        {
            case "1":
                ApplyLevel(TweakLevel.Safe);
                break;
            case "2":
                ApplyLevel(TweakLevel.Competitive);
                break;
            case "3":
                ShowIndividual();
                break;
        }
    }

    /// <summary>Read-only preview of a level, used by the --simular command line option.</summary>
    public void Preview(TweakLevel level)
    {
        var (pending, skipped, alreadyApplied) = Simulate(level);
        DrawSkipped(skipped);
        AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já aplicados.[/]");
        if (pending.Count > 0)
            DrawPending(pending);
        foreach (var (tweak, changes) in pending)
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/]");
            DrawPreview(changes);
        }
    }

    private void ApplyLevel(TweakLevel level)
    {
        var (pending, skipped, alreadyApplied) = Simulate(level);
        DrawSkipped(skipped);
        if (alreadyApplied > 0)
            AnsiConsole.MarkupLine($"[green]{alreadyApplied} ajuste(s) já estavam aplicados.[/]");
        if (pending.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nada a fazer: este nível já está aplicado.[/]");
            return;
        }

        while (true)
        {
            DrawPending(pending);
            AnsiConsole.MarkupLine($"{MenuItem("A", "Aplicar todos")}   {MenuItem("P", "Personalizar (tirar itens)")}   {MenuItem("D", "Ver detalhes")}   {MenuItem(0, "Voltar")}");
            switch (ReadChoice()?.ToUpperInvariant())
            {
                case "A":
                    if (Confirm($"Aplicar {pending.Count} ajuste(s) com ponto de restauração e backup?"))
                        RunBatch(pending);
                    else
                        log.Info("Aplicação em lote cancelada.");
                    return;
                case "P":
                    AnsiConsole.Write("Números para NÃO aplicar (ex.: 2 5 9): ");
                    var remove = (Console.ReadLine() ?? "").Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(text => int.TryParse(text, out var number) ? number - 1 : -1)
                        .Where(index => index >= 0 && index < pending.Count).ToHashSet();
                    pending = pending.Where((_, index) => !remove.Contains(index)).ToList();
                    if (pending.Count == 0)
                        return;
                    break;
                case "D":
                    foreach (var (tweak, changes) in pending)
                    {
                        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tweak.Name)}[/] [grey]— {Markup.Escape(tweak.Description)}[/]");
                        DrawPreview(changes);
                    }
                    break;
                default:
                    return;
            }
        }
    }

    private (List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> Pending, List<(TweakDefinition Tweak, string Reason)> Skipped, int AlreadyApplied)
        Simulate(TweakLevel level)
    {
        var candidates = tweaks.Where(tweak => tweak.Level <= level)
            .OrderBy(tweak => tweak.Level).ThenBy(tweak => tweak.Category, StringComparer.CurrentCulture).ThenBy(tweak => tweak.Name, StringComparer.CurrentCulture)
            .ToArray();
        var pending = new List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)>();
        var skipped = new List<(TweakDefinition Tweak, string Reason)>();
        var alreadyApplied = 0;

        AnsiConsole.Status().Spinner(Spinner.Known.Dots).SpinnerStyle(new Style(new Color(168, 85, 247))).Start("Simulando os ajustes...", _ =>
        {
            foreach (var tweak in candidates)
            {
                if (engine.CheckCompatibility(tweak) is { } reason)
                {
                    skipped.Add((tweak, reason));
                    continue;
                }
                try
                {
                    var changes = engine.Detect(tweak);
                    if (changes.Count == 0)
                        alreadyApplied++;
                    else
                        pending.Add((tweak, changes));
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    log.Warning($"Simulação de {tweak.Id} falhou: {error.Message}");
                    skipped.Add((tweak, error.Message));
                }
            }
        });
        return (pending, skipped, alreadyApplied);
    }

    private void RunBatch(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
    {
        var reviewed = pending.ToDictionary(item => item.Tweak.Id, item => item.Changes);
        IReadOnlyList<BatchResult> results = [];
        AnsiConsole.Progress().Columns(new TaskDescriptionColumn(), new ProgressBarColumn().CompletedStyle(new Style(new Color(168, 85, 247))), new PercentageColumn())
            .Start(context =>
            {
                var task = context.AddTask("Criando ponto de restauração...", maxValue: pending.Count);
                var started = false;
                results = engine.ApplyBatch(pending.Select(item => item.Tweak).ToArray(), reviewed, tweak =>
                {
                    if (started)
                        task.Increment(1);
                    started = true;
                    task.Description = Markup.Escape(tweak.Name);
                });
                task.Value = task.MaxValue;
                task.Description = "Concluído";
            });

        var ok = results.Where(result => result.Error is null).ToArray();
        var failed = results.Where(result => result.Error is not null).ToArray();
        AnsiConsole.MarkupLine($"[bold green]{ok.Length} ajuste(s) aplicados[/]{(failed.Length > 0 ? $" · [bold red]{failed.Length} falharam (e foram desfeitos)[/]" : "")}");
        foreach (var failure in failed)
            AnsiConsole.MarkupLine($"  [red]✗[/] {Markup.Escape(failure.Tweak.Name)}: [grey]{Markup.Escape(failure.Error!)}[/]");
        if (ok.Any(result => result.Tweak.RequiresReboot))
            AnsiConsole.MarkupLine("[yellow]Reinicie o PC para concluir os ajustes que precisam de reinício.[/]");
        AnsiConsole.MarkupLine("[grey]Para desfazer tudo: menu [[8]] Reverter / Backups.[/]");
    }

    private static void DrawPending(List<(TweakDefinition Tweak, IReadOnlyList<SettingChange> Changes)> pending)
    {
        var table = new Table().Border(TableBorder.Rounded).BorderColor(new Color(124, 58, 237))
            .AddColumn(new TableColumn("#").RightAligned()).AddColumn("Nível").AddColumn("Categoria").AddColumn("Ajuste")
            .AddColumn(new TableColumn("Alterações").RightAligned()).AddColumn("Reinício");
        for (var index = 0; index < pending.Count; index++)
        {
            var (tweak, changes) = pending[index];
            table.AddRow(
                (index + 1).ToString(CultureInfo.InvariantCulture),
                tweak.Level == TweakLevel.Safe ? "[green]1[/]" : "[yellow]2[/]",
                Markup.Escape(tweak.Category),
                $"{Markup.Escape(tweak.Name)}\n[grey]Risco: {Markup.Escape(tweak.Risk)}[/]",
                changes.Count.ToString(CultureInfo.InvariantCulture),
                tweak.RequiresReboot ? "[yellow]sim[/]" : "não");
        }
        AnsiConsole.Write(table);
    }

    private static void DrawSkipped(List<(TweakDefinition Tweak, string Reason)> skipped)
    {
        foreach (var (tweak, reason) in skipped)
            AnsiConsole.MarkupLine($"[grey]– {Markup.Escape(tweak.Name)} não se aplica: {Markup.Escape(reason)}[/]");
    }

    private void ShowIndividual()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Ajustes individuais[/]");
        for (var index = 0; index < tweaks.Count; index++)
            AnsiConsole.MarkupLine($"{MenuItem(index + 1, tweaks[index].Name)} [grey]({Markup.Escape(tweaks[index].Category)}, nível {(int)tweaks[index].Level + 1})[/]");
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
        AnsiConsole.MarkupLine($"[grey]Risco: {Markup.Escape(tweak.Risk)}[/]");
        if (engine.CheckCompatibility(tweak) is { } reason)
        {
            AnsiConsole.MarkupLine($"[yellow]Não se aplica a este PC: {Markup.Escape(reason)}.[/]");
            return;
        }
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
        if (tweak.RequiresReboot)
            AnsiConsole.MarkupLine("[yellow]Reinicie o PC para concluir.[/]");
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
        AnsiConsole.MarkupLine("[green]Reversão concluída. Reinicie o PC para desfazer os ajustes que dependem de reinício.[/]");
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
            AnsiConsole.WriteLine("O estado já corresponde ao esperado.");
        foreach (var change in changes)
            AnsiConsole.MarkupLine($"  {Markup.Escape(change.Target)}\n  [grey]{Markup.Escape(change.Current)}[/] → [#C084FC]{Markup.Escape(change.Expected)}[/]");
    }

    private static string MenuItem(int index, string name) => MenuItem(index.ToString(CultureInfo.InvariantCulture), name);

    private static string MenuItem(string key, string name) => $"[bold #C084FC][[{key}]][/] {Markup.Escape(name)}";

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
